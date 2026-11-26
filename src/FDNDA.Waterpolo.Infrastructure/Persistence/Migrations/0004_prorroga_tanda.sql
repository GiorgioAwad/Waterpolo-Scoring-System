-- @desactivar-claves-foraneas
-- R8: prórroga y tanda de penales.
--
-- La etapa «Shootout», el tipo de incidencia «ShootoutAttempt» y los cambios de estado de la tanda
-- no caben en los CHECK de 0001. SQLite no permite cambiar un CHECK: se reconstruyen match,
-- match_incident y match_status_change con el procedimiento oficial (tabla nueva, copia, borrado,
-- renombre), con las claves foráneas desactivadas por el ejecutor de migraciones, que comprueba
-- PRAGMA foreign_key_check antes de confirmar. Las filas se copian tal cual: no se reescribe
-- ninguna incidencia, revisión ni cambio de estado.

-- Reglas: periodos y duración de la prórroga (pendientes de aprobación técnica). Por defecto, sin prórroga.
ALTER TABLE tournament ADD COLUMN rules_overtime_period_count INTEGER NOT NULL DEFAULT 0
    CHECK (rules_overtime_period_count >= 0);
ALTER TABLE tournament ADD COLUMN rules_overtime_period_seconds INTEGER NOT NULL DEFAULT 180
    CHECK (rules_overtime_period_seconds > 0);
ALTER TABLE match_ruleset_snapshot ADD COLUMN overtime_period_count INTEGER NOT NULL DEFAULT 0;
ALTER TABLE match_ruleset_snapshot ADD COLUMN overtime_period_seconds INTEGER NOT NULL DEFAULT 180;

-- Este disparador consulta match_incident: se quita antes de reconstruirla y se recrea al final.
DROP TRIGGER trg_incident_revision_validate_insert;

-- ---------- match ----------
CREATE TABLE match_new (
    id                  TEXT PRIMARY KEY NOT NULL,
    tournament_id       TEXT NOT NULL REFERENCES tournament (id),
    tournament_name     TEXT NOT NULL,
    label               TEXT NOT NULL,
    scheduled_at_utc    TEXT NULL,
    status              TEXT NOT NULL CHECK (status IN ('Scheduled', 'InProgress', 'Finished')),
    stage               TEXT NOT NULL CHECK (stage IN ('PreMatch', 'Period', 'Break', 'Ended', 'Shootout')),
    current_period      INTEGER NOT NULL CHECK (current_period >= 0),
    home_team_id        TEXT NOT NULL REFERENCES team (id),
    home_code           TEXT NOT NULL,
    home_name           TEXT NOT NULL,
    home_color_hex      TEXT NOT NULL,
    home_cap_color      TEXT NOT NULL,
    away_team_id        TEXT NOT NULL REFERENCES team (id),
    away_code           TEXT NOT NULL,
    away_name           TEXT NOT NULL,
    away_color_hex      TEXT NOT NULL,
    away_cap_color      TEXT NOT NULL,
    version             INTEGER NOT NULL CHECK (version >= 1),
    created_at_utc      TEXT NOT NULL,
    updated_at_utc      TEXT NOT NULL,
    CHECK (home_team_id <> away_team_id)
);

INSERT INTO match_new (
    id, tournament_id, tournament_name, label, scheduled_at_utc, status, stage, current_period,
    home_team_id, home_code, home_name, home_color_hex, home_cap_color,
    away_team_id, away_code, away_name, away_color_hex, away_cap_color,
    version, created_at_utc, updated_at_utc)
SELECT
    id, tournament_id, tournament_name, label, scheduled_at_utc, status, stage, current_period,
    home_team_id, home_code, home_name, home_color_hex, home_cap_color,
    away_team_id, away_code, away_name, away_color_hex, away_cap_color,
    version, created_at_utc, updated_at_utc
FROM match;

DROP TABLE match;
ALTER TABLE match_new RENAME TO match;

-- Un solo partido en juego a la vez.
CREATE UNIQUE INDEX ux_match_single_in_progress ON match (status) WHERE status = 'InProgress';

-- ---------- match_incident ----------
CREATE TABLE match_incident_new (
    id                  TEXT PRIMARY KEY NOT NULL,
    match_id            TEXT NOT NULL REFERENCES match (id),
    sequence            INTEGER NOT NULL CHECK (sequence >= 1),
    type                TEXT NOT NULL CHECK (type IN ('Goal', 'Exclusion', 'PersonalFoul', 'Penalty', 'Timeout', 'ShootoutAttempt')),
    period              INTEGER NOT NULL CHECK (period >= 1),
    clock_source        TEXT NOT NULL CHECK (clock_source IN ('Unknown', 'Simulated', 'Replay', 'ColoradoSystem6')),
    clock_quality       TEXT NOT NULL CHECK (clock_quality IN ('Unknown', 'Live', 'Unavailable')),
    game_clock_ms       INTEGER NULL CHECK (game_clock_ms >= 0),
    shot_clock_ms       INTEGER NULL CHECK (shot_clock_ms >= 0),
    registered_at_utc   TEXT NOT NULL,
    UNIQUE (match_id, sequence),
    -- Nunca se guarda una hora de juego que no provenga de una lectura vigente.
    CHECK (clock_quality = 'Live' OR (game_clock_ms IS NULL AND shot_clock_ms IS NULL))
);

INSERT INTO match_incident_new (
    id, match_id, sequence, type, period, clock_source, clock_quality, game_clock_ms, shot_clock_ms, registered_at_utc)
SELECT
    id, match_id, sequence, type, period, clock_source, clock_quality, game_clock_ms, shot_clock_ms, registered_at_utc
FROM match_incident;

-- Con las claves foráneas desactivadas, DROP TABLE no hace el borrado implícito: los disparadores
-- que impiden borrar incidencias no se disparan y las filas ya están copiadas.
DROP TABLE match_incident;
ALTER TABLE match_incident_new RENAME TO match_incident;

CREATE TRIGGER trg_match_incident_no_delete BEFORE DELETE ON match_incident
BEGIN
    SELECT RAISE(ABORT, 'No se permite borrar incidencias confirmadas.');
END;

CREATE TRIGGER trg_match_incident_no_update BEFORE UPDATE ON match_incident
BEGIN
    SELECT RAISE(ABORT, 'No se permite modificar incidencias confirmadas; registre una revisión.');
END;

-- ---------- match_status_change ----------
CREATE TABLE match_status_change_new (
    match_id        TEXT NOT NULL REFERENCES match (id),
    sequence        INTEGER NOT NULL CHECK (sequence >= 1),
    kind            TEXT NOT NULL CHECK (kind IN ('Started', 'BreakStarted', 'PeriodStarted', 'Finished', 'Reopened',
                                                  'ShootoutStarted', 'ShootoutCancelled')),
    from_status     TEXT NOT NULL,
    to_status       TEXT NOT NULL,
    from_stage      TEXT NOT NULL,
    to_stage        TEXT NOT NULL,
    period          INTEGER NOT NULL,
    reason          TEXT NULL,
    operator_id     TEXT NOT NULL REFERENCES operator_profile (id),
    operator_name   TEXT NOT NULL,
    at_utc          TEXT NOT NULL,
    PRIMARY KEY (match_id, sequence),
    CHECK (kind NOT IN ('Reopened', 'ShootoutCancelled') OR (reason IS NOT NULL AND length(trim(reason)) > 0))
);

INSERT INTO match_status_change_new (
    match_id, sequence, kind, from_status, to_status, from_stage, to_stage, period, reason,
    operator_id, operator_name, at_utc)
SELECT
    match_id, sequence, kind, from_status, to_status, from_stage, to_stage, period, reason,
    operator_id, operator_name, at_utc
FROM match_status_change;

DROP TABLE match_status_change;
ALTER TABLE match_status_change_new RENAME TO match_status_change;

CREATE TRIGGER trg_match_status_change_no_delete BEFORE DELETE ON match_status_change
BEGIN
    SELECT RAISE(ABORT, 'No se permite borrar el historial de estados del partido.');
END;

CREATE TRIGGER trg_match_status_change_no_update BEFORE UPDATE ON match_status_change
BEGIN
    SELECT RAISE(ABORT, 'No se permite modificar el historial de estados del partido.');
END;

-- ---------- validación de revisiones (0002), ampliada a la tanda ----------
-- El lanzamiento de la tanda lleva jugador de su equipo y resultado, como el penal.
CREATE TRIGGER trg_incident_revision_validate_insert BEFORE INSERT ON incident_revision
BEGIN
    SELECT RAISE(ABORT, 'El tiempo muerto no lleva jugador; las demás incidencias requieren jugador de su equipo.')
    WHERE NOT EXISTS (
        SELECT 1 FROM match_incident i WHERE i.id = NEW.incident_id AND (
            (i.type = 'Timeout' AND NEW.roster_entry_id IS NULL) OR
            (i.type <> 'Timeout' AND EXISTS (
                SELECT 1 FROM match_roster_entry r WHERE r.id = NEW.roster_entry_id
                    AND r.match_id = i.match_id AND r.side = NEW.side))));

    SELECT RAISE(ABORT, 'Indique resultado solo para el penal y el lanzamiento de la tanda, y detalle solo para la exclusión.')
    WHERE NOT EXISTS (
        SELECT 1 FROM match_incident i WHERE i.id = NEW.incident_id
            AND ((i.type IN ('Penalty', 'ShootoutAttempt') AND NEW.penalty_result IS NOT NULL)
                OR (i.type NOT IN ('Penalty', 'ShootoutAttempt') AND NEW.penalty_result IS NULL))
            AND ((i.type = 'Exclusion' AND NEW.exclusion_kind IS NOT NULL)
                OR (i.type <> 'Exclusion' AND NEW.exclusion_kind IS NULL)));
END;
