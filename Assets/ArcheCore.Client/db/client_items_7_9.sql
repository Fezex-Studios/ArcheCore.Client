-- Client gamedata: the three items added by server patch 025, same ids.
-- Run against db/clientside_dev_db/_oggamedata.db, then rebuild gamedata.bin.
-- (Client Items has no primary key, hence WHERE NOT EXISTS.)
INSERT INTO Items (item_id, name, description, icon_name)
SELECT 7, 'Iron Ore', 'A lump of raw iron.', 'ore_iron'
WHERE NOT EXISTS (SELECT 1 FROM Items WHERE item_id = 7);
INSERT INTO Items (item_id, name, description, icon_name)
SELECT 8, 'Silverleaf', 'A common herb with silvery leaves.', 'herb_silverleaf'
WHERE NOT EXISTS (SELECT 1 FROM Items WHERE item_id = 8);
INSERT INTO Items (item_id, name, description, icon_name)
SELECT 9, 'Oak Log', 'A sturdy length of oak.', 'wood_oak'
WHERE NOT EXISTS (SELECT 1 FROM Items WHERE item_id = 9);
