-- Detalles revisables; las incidencias y revisiones anteriores se conservan sin reescritura.
ALTER TABLE incident_revision ADD COLUMN penalty_result TEXT NULL
    CHECK (penalty_result IN ('Converted', 'NotConverted'));
ALTER TABLE incident_revision ADD COLUMN exclusion_kind TEXT NULL
    CHECK (exclusion_kind IN ('Exclusion', 'Expulsion'));

-- Protege también la pertenencia del jugador al partido y lado de la incidencia.
CREATE TRIGGER trg_incident_revision_validate_insert BEFORE INSERT ON incident_revision
BEGIN
    SELECT RAISE(ABORT, 'El tiempo muerto no lleva jugador; las demás incidencias requieren jugador de su equipo.')
    WHERE NOT EXISTS (
        SELECT 1 FROM match_incident i WHERE i.id = NEW.incident_id AND (
            (i.type = 'Timeout' AND NEW.roster_entry_id IS NULL) OR
            (i.type <> 'Timeout' AND EXISTS (
                SELECT 1 FROM match_roster_entry r WHERE r.id = NEW.roster_entry_id
                    AND r.match_id = i.match_id AND r.side = NEW.side))));

    SELECT RAISE(ABORT, 'Indique resultado solo para el penal y detalle solo para la exclusión.')
    WHERE NOT EXISTS (
        SELECT 1 FROM match_incident i WHERE i.id = NEW.incident_id
            AND ((i.type = 'Penalty' AND NEW.penalty_result IS NOT NULL)
                OR (i.type <> 'Penalty' AND NEW.penalty_result IS NULL))
            AND ((i.type = 'Exclusion' AND NEW.exclusion_kind IS NOT NULL)
                OR (i.type <> 'Exclusion' AND NEW.exclusion_kind IS NULL)));
END;
