using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Together.Client.Components;
using Together.Core;
namespace Together.Tests;

public sealed class ComponentTests : BunitContext
{
    public ComponentTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }
    [Fact]
    public void BudgetShowsMissingAndReviewIndependently()
    {
        var cut = Render<BudgetView>(p => p.Add(x => x.Expenses, [4200000, 5600000, null, null, null, null]).Add(x => x.Details, true).Add(x => x.NeedsReview, true));
        Assert.Contains("Неполный бюджет", cut.Markup);
        Assert.Contains(Catalog.ReviewNotice, cut.Markup);
        Assert.Contains("Питание", cut.Markup);
        Assert.Equal(6, cut.FindAll(".cost").Count);
    }
    [Fact]
    public void ComparisonWithOneSelectionAsksForTwo()
    {
        var trip = new Trip();
        var v = new Variant { TripId = trip.Id };
        trip.Variants.Add(v);
        trip.SelectedVariantIds.Add(v.Id);
        var cut = Render<Comparison>(p => p.Add(x => x.Trip, trip));
        Assert.Contains("Выберите минимум два варианта", cut.Markup);
        Assert.Empty(cut.FindAll("table"));
    }
    [Fact]
    public void ComparisonHidesOnlyDisabledCriterionAndPreservesUnknown()
    {
        var trip = new Trip();
        trip.Variants = [new Variant { TripId = trip.Id, Name = "Первый" }, new Variant { TripId = trip.Id, Name = "Второй" }];
        trip.SelectedVariantIds = trip.Variants.Select(v => v.Id).ToList();
        trip.SelectedCriteria.Remove("kitchen");
        var cut = Render<Comparison>(p => p.Add(x => x.Trip, trip));
        Assert.Equal(6, cut.FindAll("tbody tr").Count);
        Assert.Contains("Не указано", cut.Markup);
    }
    [Fact]
    public void TripFormDoesNotSubmitInvalidDates()
    {
        var saved = false;
        var cut = Render<TripEditor>(p => p.Add(x => x.OnSave, _ => saved = true));
        cut.Find("#name").Input("Лето");
        cut.Find("#start").Input("2027-07-19");
        cut.Find("#end").Input("2027-07-12");
        cut.Find("#child-0").Input("3");
        cut.Find("form").Submit();
        Assert.False(saved);
        Assert.Equal("true", cut.Find("#end").GetAttribute("aria-invalid"));
        Assert.Equal("Лето", cut.Find("#name").GetAttribute("value"));
    }
    [Fact]
    public void VariantFormAcceptsZeroAndRejectsUnsafeUrl()
    {
        VariantDraft? saved = null;
        var cut = Render<VariantEditor>(p => p.Add(x => x.OnSave, d => saved = d));
        cut.Find("#name").Input("Апартаменты");
        cut.Find("#destination").Input("Геленджик");
        cut.Find("#accommodation").Input("Квартира");
        cut.Find("#url").Input("javascript:alert(1)");
        cut.Find("#expense-0").Input("0");
        cut.Find("form").Submit();
        Assert.Null(saved);
        Assert.Equal("true", cut.Find("#url").GetAttribute("aria-invalid"));
        cut.Find("#url").Input("https://example.com");
        cut.Find("form").Submit();
        Assert.NotNull(saved);
        Assert.Equal(0, saved.ParsedExpenses()[0]);
        Assert.Null(saved.ParsedExpenses()[1]);
    }
    [Fact]
    public void UserMarkupIsRenderedAsText()
    {
        var cut = Render<VariantCard>(p => p.Add(x => x.Variant, new Variant { Name = "<script>alert(1)</script>", Notes = "<img src=x onerror=alert(1)>" }));
        Assert.Empty(cut.FindAll("script,img"));
        Assert.Contains("<script>alert(1)</script>", cut.Find("h3").TextContent);
    }

    [Fact]
    public void PartialBudgetShowsKnownCostsWithoutPromisingRemainingMoney()
    {
        var cut = Render<BudgetView>(p => p.Add(x => x.Expenses, [9800000, null, 0, 0, 0, 0])
            .Add(x => x.BudgetLimitKopecks, 15000000).Add(x => x.ReservePercent, 10).Add(x => x.ShowAssessment, true));
        Assert.Contains("Известные расходы", cut.Markup);
        Assert.Contains("Итог может превысить лимит", cut.Markup);
        Assert.DoesNotContain("До лимита осталось", cut.Markup);
    }

    [Fact]
    public void ClarificationOpensTheSelectedVariantFieldAndUpdatesWithCriteria()
    {
        (Guid VariantId, string FieldId)? target = null;
        var variant = new Variant { Expenses = [0, 0, 0, 0, 0, 0], Crib = "unknown" };
        var cut = Render<VariantCard>(p => p.Add(x => x.Variant, variant)
            .Add(x => x.SelectedCriteria, new[] { "crib" })
            .Add(x => x.Clarify, value => target = value));
        cut.Find(".clarifications button").Click();
        Assert.Equal((variant.Id, "crib"), target);
        cut.Render(p => p.Add(x => x.SelectedCriteria, Array.Empty<string>()));
        Assert.Empty(cut.FindAll(".clarifications"));
    }

    [Fact]
    public void TripEditorValidatesAndSubmitsBudgetSettings()
    {
        TripDraft? saved = null;
        var cut = Render<TripEditor>(p => p.Add(x => x.Original, ExampleData.Create().Trips.Single())
            .Add(x => x.OnSave, value => saved = value));
        cut.Find("#budget-limit").Input("150000");
        cut.Find("#reserve-percent").Input("101");
        cut.Find("form").Submit();
        Assert.Null(saved);
        Assert.Equal("true", cut.Find("#reserve-percent").GetAttribute("aria-invalid"));
        cut.Find("#reserve-percent").Input("10");
        cut.Find("form").Submit();
        Assert.NotNull(saved);
        Assert.Equal("150000", saved.BudgetLimit);
        Assert.Equal("10", saved.ReservePercent);
    }

    [Fact]
    public void CardRecalculatesWhenTripBudgetSettingsChange()
    {
        var cut = Render<VariantCard>(p => p.Add(x => x.Variant, new Variant { Expenses = [10000, 0, 0, 0, 0, 0] })
            .Add(x => x.BudgetLimitKopecks, 20000));
        Assert.Contains("До лимита осталось — 100 ₽", cut.Markup);
        cut.Render(p => p.Add(x => x.BudgetLimitKopecks, 5000).Add(x => x.ReservePercent, 10));
        Assert.Contains("Превышение лимита — 60 ₽", cut.Markup);
        Assert.DoesNotContain("До лимита осталось", cut.Markup);
    }
}
