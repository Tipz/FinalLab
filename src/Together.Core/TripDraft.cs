using System.Globalization;

namespace Together.Core;

public sealed class TripDraft
{
    public string Name { get; set; } = "";
    public string StartDate { get; set; } = "";
    public string EndDate { get; set; } = "";
    public string Adults { get; set; } = "2";
    public string BudgetLimit { get; set; } = "";
    public string ReservePercent { get; set; } = "0";
    public List<string> ChildAges { get; set; } = [""];
    public static TripDraft From(Trip t) => new() { Name = t.Name, StartDate = t.StartDate.ToString("yyyy-MM-dd"), EndDate = t.EndDate.ToString("yyyy-MM-dd"), Adults = t.Adults.ToString(), ChildAges = t.ChildAges.Select(x => x.ToString()).ToList(), BudgetLimit = Budget.Input(t.BudgetLimitKopecks), ReservePercent = t.ReservePercent.ToString(CultureInfo.InvariantCulture) };
    public Dictionary<string, string> Validate()
    {
        var errors = new Dictionary<string, string>();
        if (!DataValidation.Text(Name.Trim(), 100))
            errors["name"] = "Укажите название, от 1 до 100 символов";
        if (!DateOnly.TryParseExact(StartDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
            errors["start"] = "Укажите дату начала";
        if (!DateOnly.TryParseExact(EndDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end) || end <= start)
            errors["end"] = "Окончание должно быть позже начала";
        if (!int.TryParse(Adults, out var adults) || adults is < 1 or > 10)
            errors["adults"] = "Укажите целое число от 1 до 10";
        if (!Budget.TryParse(BudgetLimit, out _))
            errors["budget-limit"] = "Укажите сумму от 0 до 100 000 000 ₽, не более двух знаков после запятой";
        if (!int.TryParse(ReservePercent, out var reserve) || reserve is < 0 or > 100)
            errors["reserve-percent"] = "Укажите целый процент от 0 до 100";
        for (var i = 0; i < ChildAges.Count; i++)
            if (!int.TryParse(ChildAges[i], out var age) || age is < 0 or > 17)
                errors[$"child-{i}"] = "Укажите целый возраст от 0 до 17 лет";
        if (ChildAges.Count is < 1 or > 10)
            errors["children"] = "Нужно от 1 до 10 детей";
        return errors;
    }
    public void Apply(Trip t)
    {
        var start = DateOnly.ParseExact(StartDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var end = DateOnly.ParseExact(EndDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var ages = ChildAges.Select(int.Parse).ToArray();
        var adults = int.Parse(Adults);
        if (t.StartDate != start || t.EndDate != end || t.Adults != adults || !t.ChildAges.SequenceEqual(ages))
            foreach (var v in t.Variants)
            {
                v.NeedsBudgetReview = true;
                v.Revision++;
                v.UpdatedAt = DateTimeOffset.UtcNow;
            }
        t.Name = Name.Trim();
        t.StartDate = start;
        t.EndDate = end;
        t.Adults = adults;
        Budget.TryParse(BudgetLimit, out var budgetLimit);
        t.BudgetLimitKopecks = budgetLimit;
        t.ReservePercent = int.Parse(ReservePercent, CultureInfo.InvariantCulture);
        t.ChildAges = ages;
    }
}
