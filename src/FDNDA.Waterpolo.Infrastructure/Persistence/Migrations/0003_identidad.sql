-- R7: identidad visual. Las imágenes siguen en asset (máx. 2 MB, PNG/JPEG) para que el respaldo
-- sea un único archivo coherente. Ninguna de estas columnas forma parte del registro del partido.

-- Bandera del equipo o selección (el escudo ya existía en crest_asset_id).
ALTER TABLE team ADD COLUMN flag_asset_id TEXT NULL REFERENCES asset (id);

-- Plantilla del marcador por evento: color de acento y logo de la organización (identidad FDNDA).
ALTER TABLE event_branding ADD COLUMN accent_color_hex TEXT NULL;
ALTER TABLE event_branding ADD COLUMN organizer_logo_asset_id TEXT NULL REFERENCES asset (id);

-- Auspiciadores del evento, en el orden en que se muestran.
CREATE TABLE event_sponsor (
    tournament_id   TEXT NOT NULL REFERENCES tournament (id),
    position        INTEGER NOT NULL CHECK (position BETWEEN 1 AND 8),
    name            TEXT NOT NULL CHECK (length(trim(name)) > 0),
    asset_id        TEXT NOT NULL REFERENCES asset (id),
    PRIMARY KEY (tournament_id, position)
);
