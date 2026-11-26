-- 0001_inicial: esquema base del Marcador de Waterpolo FDNDA.
-- Identificadores: GUID en TEXT. Fechas: ISO-8601 en UTC. Enumerados: nombre en TEXT.
-- No se guardan DNI, fechas de nacimiento, correos ni otros datos personales.

CREATE TABLE operator_profile (
    id              TEXT PRIMARY KEY NOT NULL,
    display_name    TEXT NOT NULL COLLATE NOCASE UNIQUE CHECK (length(trim(display_name)) > 0),
    created_at_utc  TEXT NOT NULL
);

-- Imágenes locales acotadas dentro de la base para que el respaldo sea un único archivo coherente.
CREATE TABLE asset (
    id              TEXT PRIMARY KEY NOT NULL,
    kind            TEXT NOT NULL CHECK (kind IN ('Escudo', 'Bandera', 'Auspiciador', 'LogoEvento', 'Otro')),
    file_name       TEXT NOT NULL,
    media_type      TEXT NOT NULL CHECK (media_type IN ('image/png', 'image/jpeg')),
    byte_length     INTEGER NOT NULL CHECK (byte_length BETWEEN 1 AND 2097152),
    sha256          TEXT NOT NULL,
    content         BLOB NOT NULL,
    created_at_utc  TEXT NOT NULL
);

CREATE TABLE tournament (
    id                          TEXT PRIMARY KEY NOT NULL,
    name                        TEXT NOT NULL CHECK (length(trim(name)) > 0),
    is_demo                     INTEGER NOT NULL CHECK (is_demo IN (0, 1)),
    rules_preset_name           TEXT NOT NULL,
    rules_is_demo_preset        INTEGER NOT NULL CHECK (rules_is_demo_preset IN (0, 1)),
    rules_period_count          INTEGER NOT NULL CHECK (rules_period_count >= 1),
    rules_period_seconds        INTEGER NOT NULL CHECK (rules_period_seconds > 0),
    rules_break_seconds         INTEGER NOT NULL CHECK (rules_break_seconds >= 0),
    rules_halftime_seconds      INTEGER NOT NULL CHECK (rules_halftime_seconds >= 0),
    rules_timeouts_per_team     INTEGER NOT NULL CHECK (rules_timeouts_per_team >= 0),
    rules_shot_clock_seconds    INTEGER NOT NULL CHECK (rules_shot_clock_seconds > 0),
    rules_shot_clock_alt_seconds INTEGER NOT NULL CHECK (rules_shot_clock_alt_seconds > 0),
    rules_shootout_enabled      INTEGER NOT NULL CHECK (rules_shootout_enabled IN (0, 1)),
    created_at_utc              TEXT NOT NULL
);

CREATE TABLE event_branding (
    tournament_id       TEXT PRIMARY KEY NOT NULL REFERENCES tournament (id),
    display_title       TEXT NOT NULL,
    subtitle            TEXT NULL,
    primary_color_hex   TEXT NULL,
    logo_asset_id       TEXT NULL REFERENCES asset (id)
);

CREATE TABLE team (
    id              TEXT PRIMARY KEY NOT NULL,
    code            TEXT NOT NULL COLLATE NOCASE UNIQUE CHECK (length(trim(code)) > 0),
    name            TEXT NOT NULL CHECK (length(trim(name)) > 0),
    color_hex       TEXT NOT NULL,
    cap_color       TEXT NOT NULL,
    crest_asset_id  TEXT NULL REFERENCES asset (id),
    created_at_utc  TEXT NOT NULL
);

CREATE TABLE player (
    id              TEXT PRIMARY KEY NOT NULL,
    team_id         TEXT NOT NULL REFERENCES team (id),
    full_name       TEXT NOT NULL CHECK (length(trim(full_name)) > 0),
    created_at_utc  TEXT NOT NULL
);

CREATE INDEX ix_player_team ON player (team_id);

CREATE TABLE tournament_roster_entry (
    id              TEXT PRIMARY KEY NOT NULL,
    tournament_id   TEXT NOT NULL REFERENCES tournament (id),
    team_id         TEXT NOT NULL REFERENCES team (id),
    player_id       TEXT NOT NULL REFERENCES player (id),
    cap_number      INTEGER NOT NULL CHECK (cap_number BETWEEN 1 AND 99),
    -- El mismo gorro en equipos distintos es válido; dentro del mismo equipo, no.
    UNIQUE (tournament_id, team_id, cap_number),
    UNIQUE (tournament_id, player_id)
);

CREATE TABLE match (
    id                  TEXT PRIMARY KEY NOT NULL,
    tournament_id       TEXT NOT NULL REFERENCES tournament (id),
    tournament_name     TEXT NOT NULL,
    label               TEXT NOT NULL,
    scheduled_at_utc    TEXT NULL,
    status              TEXT NOT NULL CHECK (status IN ('Scheduled', 'InProgress', 'Finished')),
    stage               TEXT NOT NULL CHECK (stage IN ('PreMatch', 'Period', 'Break', 'Ended')),
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

-- Un solo partido en juego a la vez.
CREATE UNIQUE INDEX ux_match_single_in_progress ON match (status) WHERE status = 'InProgress';

-- Reglas copiadas del evento al iniciar el partido.
CREATE TABLE match_ruleset_snapshot (
    match_id                TEXT PRIMARY KEY NOT NULL REFERENCES match (id),
    preset_name             TEXT NOT NULL,
    is_demo_preset          INTEGER NOT NULL CHECK (is_demo_preset IN (0, 1)),
    period_count            INTEGER NOT NULL,
    period_seconds          INTEGER NOT NULL,
    break_seconds           INTEGER NOT NULL,
    halftime_seconds        INTEGER NOT NULL,
    timeouts_per_team       INTEGER NOT NULL,
    shot_clock_seconds      INTEGER NOT NULL,
    shot_clock_alt_seconds  INTEGER NOT NULL,
    shootout_enabled        INTEGER NOT NULL CHECK (shootout_enabled IN (0, 1)),
    copied_at_utc           TEXT NOT NULL
);

-- Nómina del partido con nombre y gorro congelados.
CREATE TABLE match_roster_entry (
    id              TEXT PRIMARY KEY NOT NULL,
    match_id        TEXT NOT NULL REFERENCES match (id),
    side            TEXT NOT NULL CHECK (side IN ('Home', 'Away')),
    player_id       TEXT NOT NULL REFERENCES player (id),
    player_name     TEXT NOT NULL,
    cap_number      INTEGER NOT NULL CHECK (cap_number BETWEEN 1 AND 99),
    UNIQUE (match_id, side, cap_number),
    UNIQUE (match_id, player_id)
);

CREATE TABLE match_incident (
    id                  TEXT PRIMARY KEY NOT NULL,
    match_id            TEXT NOT NULL REFERENCES match (id),
    sequence            INTEGER NOT NULL CHECK (sequence >= 1),
    type                TEXT NOT NULL CHECK (type IN ('Goal', 'Exclusion', 'PersonalFoul', 'Penalty', 'Timeout')),
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

-- Historial de revisiones: la 1 es el registro original; las siguientes, correcciones o anulación.
CREATE TABLE incident_revision (
    incident_id         TEXT NOT NULL REFERENCES match_incident (id),
    revision_number     INTEGER NOT NULL CHECK (revision_number >= 1),
    kind                TEXT NOT NULL CHECK (kind IN ('Original', 'Correction', 'Void')),
    side                TEXT NOT NULL CHECK (side IN ('Home', 'Away')),
    roster_entry_id     TEXT NULL REFERENCES match_roster_entry (id),
    reason              TEXT NULL,
    operator_id         TEXT NOT NULL REFERENCES operator_profile (id),
    operator_name       TEXT NOT NULL,
    at_utc              TEXT NOT NULL,
    PRIMARY KEY (incident_id, revision_number),
    CHECK ((kind = 'Original' AND revision_number = 1) OR (kind <> 'Original' AND revision_number > 1)),
    CHECK (kind = 'Original' OR (reason IS NOT NULL AND length(trim(reason)) > 0))
);

CREATE TABLE match_status_change (
    match_id        TEXT NOT NULL REFERENCES match (id),
    sequence        INTEGER NOT NULL CHECK (sequence >= 1),
    kind            TEXT NOT NULL CHECK (kind IN ('Started', 'BreakStarted', 'PeriodStarted', 'Finished', 'Reopened')),
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
    CHECK (kind <> 'Reopened' OR (reason IS NOT NULL AND length(trim(reason)) > 0))
);

-- Identificadores de comandos ya aplicados (idempotencia ante reintentos).
CREATE TABLE processed_command (
    command_id          TEXT PRIMARY KEY NOT NULL,
    match_id            TEXT NOT NULL REFERENCES match (id),
    command_type        TEXT NOT NULL,
    applied_at_utc      TEXT NOT NULL,
    resulting_version   INTEGER NOT NULL
);

-- Sin borrado físico ni reescritura de incidencias confirmadas.
CREATE TRIGGER trg_match_incident_no_delete BEFORE DELETE ON match_incident
BEGIN
    SELECT RAISE(ABORT, 'No se permite borrar incidencias confirmadas.');
END;

CREATE TRIGGER trg_match_incident_no_update BEFORE UPDATE ON match_incident
BEGIN
    SELECT RAISE(ABORT, 'No se permite modificar incidencias confirmadas; registre una revisión.');
END;

CREATE TRIGGER trg_incident_revision_no_delete BEFORE DELETE ON incident_revision
BEGIN
    SELECT RAISE(ABORT, 'No se permite borrar revisiones de incidencias.');
END;

CREATE TRIGGER trg_incident_revision_no_update BEFORE UPDATE ON incident_revision
BEGIN
    SELECT RAISE(ABORT, 'No se permite modificar revisiones de incidencias.');
END;

CREATE TRIGGER trg_match_status_change_no_delete BEFORE DELETE ON match_status_change
BEGIN
    SELECT RAISE(ABORT, 'No se permite borrar el historial de estados del partido.');
END;

CREATE TRIGGER trg_match_status_change_no_update BEFORE UPDATE ON match_status_change
BEGIN
    SELECT RAISE(ABORT, 'No se permite modificar el historial de estados del partido.');
END;
