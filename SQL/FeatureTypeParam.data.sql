INSERT INTO FeatureTypeParam (FeatureTypeId, ParameterKey) VALUES 
	((Select Id FROM FeatureType WHERE name like 'AtrRatio'), 'fast'),
	((Select Id FROM FeatureType WHERE name like 'AtrVsOpen'), 'slow'),
	((Select Id FROM FeatureType WHERE name like 'AtrVsOpen'), 'period'),
	((Select Id FROM FeatureType WHERE name like 'OpenVsSma'), 'period'),
	((Select Id FROM FeatureType WHERE name like 'RangeVsAvgRange'), 'range'),
	((Select Id FROM FeatureType WHERE name like 'Rsi'), 'range'),
	((Select Id FROM FeatureType WHERE name like 'SmaRatio'), 'fast'),
	((Select Id FROM FeatureType WHERE name like 'SmaRatio'), 'slow'),
	((Select Id FROM FeatureType WHERE name like 'VolumeSma'), 'period'),
	((Select Id FROM FeatureType WHERE name like 'VolumeZScore'), 'period')