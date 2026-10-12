using System.Text.Json;
using System.Text.Json.Nodes;
using Together.Core;

namespace Together.Tests;

public sealed class BudgetPlanningTests
{
    [Fact]
    public void ReserveUsesKopecksAndRoundsUp()
    {
        var result = Budget.Assess([1, 0, 0, 0, 0, 0], 2, 10);
        Assert.Equal(1, result.ReserveKopecks);
        Assert.Equal(2, result.TotalKopecks);
        Assert.Equal(BudgetLimitStatus.AtLimit, result.Status);
    }

    [Theory]
    [InlineData(14300000L, BudgetLimitStatus.AtLimit, 0L)]
    [InlineData(15000000L, BudgetLimitStatus.WithinLimit, 700000L)]
    [InlineData(14299999L, BudgetLimitStatus.Exceeded, -1L)]
    public void FullBudgetIncludesReserve(long limit, BudgetLimitStatus status, long remaining)
    {
        var result = Budget.Assess([13000000, 0, 0, 0, 0, 0], limit, 10);
        Assert.Equal(14300000, result.TotalKopecks);
        Assert.Equal(status, result.Status);
        Assert.Equal(remaining, result.RemainingKopecks);
    }

    [Fact]
    public void IncompleteOrUnreviewedBudgetCannotBeDeclaredWithinLimit()
    {
        Assert.Equal(BudgetLimitStatus.Uncertain, Budget.Assess([100, null, 0, 0, 0, 0], 1000, 0).Status);
        Assert.Equal(BudgetLimitStatus.Uncertain, Budget.Assess([100, 0, 0, 0, 0, 0], 1000, 0, true).Status);
        Assert.Equal(BudgetLimitStatus.Exceeded, Budget.Assess([100, null, 0, 0, 0, 0], 99, 0).Status);
        Assert.Equal(BudgetLimitStatus.Exceeded, Budget.Assess([100, 0, 0, 0, 0, 0], 99, 0, true).Status);
    }

    [Fact]
    public void ZeroLimitAndMissingLimitAreDifferentAndMaximumExpensesDoNotOverflow()
    {
        Assert.Equal(BudgetLimitStatus.NotSet, Budget.Assess([0, 0, 0, 0, 0, 0], null, 0).Status);
        Assert.Equal(BudgetLimitStatus.AtLimit, Budget.Assess([0, 0, 0, 0, 0, 0], 0, 0).Status);
        Assert.Equal(120000000000L, Budget.Assess(Enumerable.Repeat<long?>(10000000000L, 6).ToArray(), 0, 100).TotalKopecks);
    }

    [Fact]
    public void BudgetSettingsDoNotInvalidateExpenses()
    {
        var trip = ExampleData.Create().Trips.Single();
        var expenses = trip.Variants.Select(v => v.Expenses.ToArray()).ToArray();
        var draft = TripDraft.From(trip);
        draft.BudgetLimit = "150 000,01";
        draft.ReservePercent = "10";
        Assert.Empty(draft.Validate());
        draft.Apply(trip);
        Assert.Equal(15000001, trip.BudgetLimitKopecks);
        Assert.Equal(10, trip.ReservePercent);
        for (var i = 0; i < expenses.Length; i++)
        {
            Assert.Equal(expenses[i], trip.Variants[i].Expenses);
            Assert.False(trip.Variants[i].NeedsBudgetReview);
        }
    }

    [Theory]
    [InlineData("-1", "0", "budget-limit")]
    [InlineData("1.234", "0", "budget-limit")]
    [InlineData("100000000.01", "0", "budget-limit")]
    [InlineData("", "101", "reserve-percent")]
    [InlineData("", "-1", "reserve-percent")]
    [InlineData("", "1.5", "reserve-percent")]
    public void DraftRejectsInvalidSettings(string limit, string reserve, string field)
    {
        var draft = TripDraft.From(ExampleData.Create().Trips.Single());
        draft.BudgetLimit = limit;
        draft.ReservePercent = reserve;
        Assert.Contains(field, draft.Validate().Keys);
    }

    [Fact]
    public void LegacyWorkspaceDefaultsToNoLimitAndZeroReserve()
    {
        var node = JsonNode.Parse(JsonSerializer.Serialize(ExampleData.Create(), Workspace.JsonOptions))!;
        foreach (var trip in node["trips"]!.AsArray())
        {
            trip!.AsObject().Remove("budgetLimitKopecks");
            trip.AsObject().Remove("reservePercent");
        }
        var imported = Workspace.Parse(node.ToJsonString());
        Assert.Null(imported.Trips[0].BudgetLimitKopecks);
        Assert.Equal(0, imported.Trips[0].ReservePercent);
    }

    [Fact]
    public void ClarificationsPrioritizeExpensesAndRespectCriteria()
    {
        var variant = new Variant { Expenses = [0, null, 0, 0, 0, 0], Transfers = 0, Kitchen = "no", NeedsBudgetReview = true };
        var items = VariantClarifications.For(variant, ["transfers", "kitchen", "crib"]);
        Assert.Equal(["expense-1", "budget-reviewed", "crib"], items.Select(x => x.FieldId).ToArray());
        variant.Expenses[1] = 0;
        variant.Crib = "no";
        variant.NeedsBudgetReview = false;
        Assert.Empty(VariantClarifications.For(variant, ["transfers", "kitchen", "crib"]));
    }
}
