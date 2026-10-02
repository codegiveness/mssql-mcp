SELECT NEXT VALUE FOR dbo.ReviewSequence OVER (ORDER BY v.Id) FROM (VALUES (1), (2)) AS v(Id);
