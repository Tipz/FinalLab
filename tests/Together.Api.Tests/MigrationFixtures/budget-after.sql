\set ON_ERROR_STOP on
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM trips WHERE "Id" = '00000000-0000-0000-0000-000000000102' AND "Name" = 'Migration fixture' AND "Revision" = 7 AND "BudgetLimitKopecks" IS NULL AND "ReservePercent" = 0) THEN
        RAISE EXCEPTION 'Migration did not preserve trip/default settings';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM trip_children WHERE "TripId" = '00000000-0000-0000-0000-000000000102' AND "Age" = 4) OR
       NOT EXISTS (SELECT 1 FROM variants WHERE "Id" = '00000000-0000-0000-0000-000000000103' AND "Revision" = 3 AND NOT "NeedsBudgetReview") OR
       (SELECT count(*) FROM variant_expenses WHERE "VariantId" = '00000000-0000-0000-0000-000000000103' AND (("Category" = 'travel' AND "AmountKopecks" = 9800000) OR ("Category" = 'food' AND "AmountKopecks" IS NULL) OR ("Category" = 'other' AND "AmountKopecks" = 0))) <> 3 OR
       NOT EXISTS (SELECT 1 FROM trip_selected_variants WHERE "TripId" = '00000000-0000-0000-0000-000000000102') OR
       NOT EXISTS (SELECT 1 FROM trip_selected_criteria WHERE "TripId" = '00000000-0000-0000-0000-000000000102' AND "Criterion" = 'budget') THEN
        RAISE EXCEPTION 'Migration did not preserve related data';
    END IF;
    BEGIN
        UPDATE trips SET "BudgetLimitKopecks" = -1 WHERE "Id" = '00000000-0000-0000-0000-000000000102';
        RAISE EXCEPTION 'Negative limit accepted';
    EXCEPTION WHEN check_violation THEN NULL;
    END;
    BEGIN
        UPDATE trips SET "BudgetLimitKopecks" = 10000000001 WHERE "Id" = '00000000-0000-0000-0000-000000000102';
        RAISE EXCEPTION 'Excessive limit accepted';
    EXCEPTION WHEN check_violation THEN NULL;
    END;
    BEGIN
        UPDATE trips SET "ReservePercent" = 101 WHERE "Id" = '00000000-0000-0000-0000-000000000102';
        RAISE EXCEPTION 'Excessive reserve accepted';
    EXCEPTION WHEN check_violation THEN NULL;
    END;
    BEGIN
        UPDATE trips SET "ReservePercent" = -1 WHERE "Id" = '00000000-0000-0000-0000-000000000102';
        RAISE EXCEPTION 'Negative reserve accepted';
    EXCEPTION WHEN check_violation THEN NULL;
    END;
END $$;
SELECT 'PASS migration: defaults, trip/related data and constraints' AS result;
