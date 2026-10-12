-- Run only against an isolated test database at InitialPostgreSql.
\set ON_ERROR_STOP on
BEGIN;
INSERT INTO "AspNetUsers" ("Id", "Email", "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
VALUES ('00000000-0000-0000-0000-000000000101', 'migration@example.test', false, false, false, false, 0);
INSERT INTO trips ("Id", "OwnerId", "Name", "StartDate", "EndDate", "Adults", "CreatedAt", "UpdatedAt", "Revision")
VALUES ('00000000-0000-0000-0000-000000000102', '00000000-0000-0000-0000-000000000101', 'Migration fixture', '2027-07-01', '2027-07-08', 2, '2026-10-12T00:00:00Z', '2026-10-12T00:00:00Z', 7);
INSERT INTO trip_children ("TripId", "Position", "Age") VALUES ('00000000-0000-0000-0000-000000000102', 0, 4);
INSERT INTO variants ("Id", "TripId", "Name", "Destination", "Accommodation", "SourceUrl", "RoadDescription", "Notes", "Kitchen", "Crib", "Playground", "DistanceTarget", "NeedsBudgetReview", "CreatedAt", "UpdatedAt", "Revision")
VALUES ('00000000-0000-0000-0000-000000000103', '00000000-0000-0000-0000-000000000102', 'Test house', 'Test town', 'House', '', '', '', 'unknown', 'no', 'yes', '', false, '2026-10-12T00:00:00Z', '2026-10-12T00:00:00Z', 3);
INSERT INTO variant_expenses ("VariantId", "Category", "AmountKopecks")
VALUES ('00000000-0000-0000-0000-000000000103', 'travel', 9800000),
       ('00000000-0000-0000-0000-000000000103', 'food', NULL),
       ('00000000-0000-0000-0000-000000000103', 'other', 0);
INSERT INTO trip_selected_variants ("TripId", "VariantId", "Position") VALUES ('00000000-0000-0000-0000-000000000102', '00000000-0000-0000-0000-000000000103', 0);
INSERT INTO trip_selected_criteria ("TripId", "Criterion", "Position") VALUES ('00000000-0000-0000-0000-000000000102', 'budget', 0);
COMMIT;
