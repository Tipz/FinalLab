using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Together.Core;

namespace Together.Api.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityUserContext<ApplicationUser, Guid>(options)
{
    public DbSet<TripEntity> Trips => Set<TripEntity>();
    public DbSet<TripChildEntity> TripChildren => Set<TripChildEntity>();
    public DbSet<VariantEntity> Variants => Set<VariantEntity>();
    public DbSet<VariantExpenseEntity> VariantExpenses => Set<VariantExpenseEntity>();
    public DbSet<SelectedVariantEntity> SelectedVariants => Set<SelectedVariantEntity>();
    public DbSet<SelectedCriterionEntity> SelectedCriteria => Set<SelectedCriterionEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureTrip(modelBuilder);
        ConfigureVariant(modelBuilder);
        ConfigureSelections(modelBuilder);
    }

    private static void ConfigureTrip(ModelBuilder modelBuilder)
    {
        var trip = modelBuilder.Entity<TripEntity>();
        trip.ToTable("trips", table =>
        {
            table.HasCheckConstraint("ck_trips_name", "char_length(\"Name\") BETWEEN 1 AND 100");
            table.HasCheckConstraint("ck_trips_dates", "\"EndDate\" > \"StartDate\"");
            table.HasCheckConstraint("ck_trips_adults", "\"Adults\" BETWEEN 1 AND 10");
            table.HasCheckConstraint("ck_trips_revision", "\"Revision\" >= 0");
            table.HasCheckConstraint("ck_trips_budget_limit", "\"BudgetLimitKopecks\" IS NULL OR \"BudgetLimitKopecks\" BETWEEN 0 AND 10000000000");
            table.HasCheckConstraint("ck_trips_reserve_percent", "\"ReservePercent\" BETWEEN 0 AND 100");
        });
        trip.HasKey(x => x.Id);
        trip.Property(x => x.Name).HasMaxLength(100).IsRequired();
        trip.Property(x => x.Revision).IsConcurrencyToken();
        trip.Property(x => x.ReservePercent).HasDefaultValue(0);
        trip.HasIndex(x => new { x.OwnerId, x.UpdatedAt });
        trip.HasOne(x => x.Owner).WithMany(x => x.Trips).HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Cascade);

        var child = modelBuilder.Entity<TripChildEntity>();
        child.ToTable("trip_children", table => table.HasCheckConstraint("ck_trip_children_age", "\"Age\" BETWEEN 0 AND 17"));
        child.HasKey(x => new { x.TripId, x.Position });
        child.Property(x => x.Position).ValueGeneratedNever();
        child.HasOne(x => x.Trip).WithMany(x => x.Children).HasForeignKey(x => x.TripId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureVariant(ModelBuilder modelBuilder)
    {
        var variant = modelBuilder.Entity<VariantEntity>();
        variant.ToTable("variants", table =>
        {
            table.HasCheckConstraint("ck_variants_travel_minutes", "\"TravelMinutes\" IS NULL OR \"TravelMinutes\" BETWEEN 0 AND 100000");
            table.HasCheckConstraint("ck_variants_transfers", "\"Transfers\" IS NULL OR \"Transfers\" BETWEEN 0 AND 20");
            table.HasCheckConstraint("ck_variants_distance", "\"DistanceMeters\" IS NULL OR \"DistanceMeters\" BETWEEN 0 AND 1000000");
            table.HasCheckConstraint("ck_variants_amenities", "\"Kitchen\" IN ('yes','no','unknown') AND \"Crib\" IN ('yes','no','unknown') AND \"Playground\" IN ('yes','no','unknown')");
            table.HasCheckConstraint("ck_variants_revision", "\"Revision\" >= 0");
        });
        variant.HasKey(x => x.Id);
        variant.HasAlternateKey(x => new { x.TripId, x.Id });
        variant.Property(x => x.Name).HasMaxLength(150).IsRequired();
        variant.Property(x => x.Destination).HasMaxLength(150).IsRequired();
        variant.Property(x => x.Accommodation).HasMaxLength(150).IsRequired();
        variant.Property(x => x.SourceUrl).HasMaxLength(2048).IsRequired();
        variant.Property(x => x.RoadDescription).HasMaxLength(2000).IsRequired();
        variant.Property(x => x.Notes).HasMaxLength(2000).IsRequired();
        variant.Property(x => x.Kitchen).HasMaxLength(7).IsRequired();
        variant.Property(x => x.Crib).HasMaxLength(7).IsRequired();
        variant.Property(x => x.Playground).HasMaxLength(7).IsRequired();
        variant.Property(x => x.DistanceTarget).HasMaxLength(100).IsRequired();
        variant.Property(x => x.Revision).IsConcurrencyToken();
        variant.HasOne(x => x.Trip).WithMany(x => x.Variants).HasForeignKey(x => x.TripId).OnDelete(DeleteBehavior.Cascade);

        var expense = modelBuilder.Entity<VariantExpenseEntity>();
        expense.ToTable("variant_expenses", table =>
        {
            table.HasCheckConstraint("ck_variant_expenses_category", $"\"Category\" IN ('{string.Join("','", Catalog.ExpenseKeys)}')");
            table.HasCheckConstraint("ck_variant_expenses_amount", "\"AmountKopecks\" IS NULL OR \"AmountKopecks\" BETWEEN 0 AND 10000000000");
        });
        expense.HasKey(x => new { x.VariantId, x.Category });
        expense.Property(x => x.Category).HasMaxLength(32);
        expense.HasOne(x => x.Variant).WithMany(x => x.Expenses).HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureSelections(ModelBuilder modelBuilder)
    {
        var selectedVariant = modelBuilder.Entity<SelectedVariantEntity>();
        selectedVariant.ToTable("trip_selected_variants");
        selectedVariant.HasKey(x => new { x.TripId, x.VariantId });
        selectedVariant.HasIndex(x => new { x.TripId, x.Position }).IsUnique();
        selectedVariant.HasOne(x => x.Trip).WithMany(x => x.SelectedVariants).HasForeignKey(x => x.TripId).OnDelete(DeleteBehavior.Cascade);
        selectedVariant.HasOne(x => x.Variant).WithMany().HasForeignKey(x => new { x.TripId, x.VariantId }).HasPrincipalKey(x => new { x.TripId, x.Id }).OnDelete(DeleteBehavior.Cascade);

        var criterion = modelBuilder.Entity<SelectedCriterionEntity>();
        criterion.ToTable("trip_selected_criteria", table => table.HasCheckConstraint("ck_selected_criteria_value", $"\"Criterion\" IN ('{string.Join("','", Catalog.Criteria)}')"));
        criterion.HasKey(x => new { x.TripId, x.Criterion });
        criterion.HasIndex(x => new { x.TripId, x.Position }).IsUnique();
        criterion.Property(x => x.Criterion).HasMaxLength(32);
        criterion.HasOne(x => x.Trip).WithMany(x => x.SelectedCriteria).HasForeignKey(x => x.TripId).OnDelete(DeleteBehavior.Cascade);
    }
}
