-- @desactivar-claves-foraneas
-- R10: reprogramar, actualizar la nómina y anular partidos aún no iniciados.
--
-- El estado «Cancelled» (anulado) y los cambios «Rescheduled», «RosterReplaced» y «Cancelled» no
-- caben en los CHECK de 0004. Como en 0004, se reconstruyen match y match_status_change con el
-- procedimiento oficial de SQLite (tabla nueva, copia, borrado, renombre), con las claves foráneas
-- desactivadas por el ejecutor de migraciones, que comprueba PRAGMA foreign_key_check antes de
-- confirmar. Las filas se copian tal cual.

-- ---------- match ----------
CREATE TABLE match_new (
    id                  TEXT PRIMARY KEY NOT NULL,
    tournament_id       TEXT NOT NULL REFERENCES tournament (id),
    tournament_name     TEXT NOT NULL,
    label               TEXT NOT NULL,
    scheduled_at_utc    TEXT NULL,
    status              TEXT NOT NULL CHECK (status IN ('Scheduled', 'InProgress', 'Finished', 'Cancelled')),
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
    CHECK (home_team_id <> away_team_id),
    -- Solo se anula un partido que no llegó a iniciarse.
    CHECK (status <> 'Cancelled' OR (stage = 'PreMatch' AND current_period = 0))
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

-- ---------- match_status_change ----------
CREATE TABLE match_status_change_new (
    match_id        TEXT NOT NULL REFERENCES match (id),
    sequence        INTEGER NOT NULL CHECK (sequence >= 1),
    kind            TEXT NOT NULL CHECK (kind IN ('Started', 'BreakStarted', 'PeriodStarted', 'Finished', 'Reopened',
                                                  'ShootoutStarted', 'ShootoutCancelled',
                                                  'Rescheduled', 'RosterReplaced', 'Cancelled')),
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
    CHECK (kind NOT IN ('Reopened', 'ShootoutCancelled', 'Cancelled') OR (reason IS NOT NULL AND length(trim(reason)) > 0))
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

-- ---------- match_roster_entry ----------
-- La nómina congelada solo se reemplaza mientras el partido está programado. Desde que se inicia
-- (o si se anuló) es parte del registro: no se borra ni se modifica.
CREATE TRIGGER trg_match_roster_entry_no_delete BEFORE DELETE ON match_roster_entry
WHEN (SELECT status FROM match WHERE id = OLD.match_id) <> 'Scheduled'
BEGIN
    SELECT RAISE(ABORT, 'No se permite cambiar la nómina de un partido iniciado, finalizado o anulado.');
END;

CREATE TRIGGER trg_match_roster_entry_no_update BEFORE UPDATE ON match_roster_entry
BEGIN
    SELECT RAISE(ABORT, 'No se permite modificar la nómina de un partido; se reemplaza entera antes de iniciarlo.');
END;
