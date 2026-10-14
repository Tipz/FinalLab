using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;
using Together.Client.Storage;
using Together.Client.Analytics;
using Together.Core;
using TripVariant = Together.Core.Variant;

namespace Together.Client.Components;

public partial class TripWorkspace
{
    [Parameter]
    public Guid? TripId
    {
        get; set;
    }
    [Parameter]
    public bool IsComparison
    {
        get; set;
    }
    [Inject] public ApiTripStore Store { get; set; } = default!;
    [Inject] public NavigationManager Navigation { get; set; } = default!;
    [Inject] public AuthClient Auth { get; set; } = default!;
    [Inject] public BrowserStore LegacyStore { get; set; } = default!;
    [Inject] public AnalyticsClient Analytics { get; set; } = default!;
    [Inject] public IJSRuntime Js { get; set; } = default!;
    private Workspace data = new();
    private Guid? currentId;
    private Trip? Current => data.Trips.FirstOrDefault(t => t.Id == currentId);
    private Trip? NavigationTrip => Current ?? data.Trips.MinBy(t => t.CreatedAt);
    private bool HasExample => data.Trips.Any(t => t.Name == ExampleData.TripName);
    private bool loading = true, readFailed, busy, conflict;
    private bool legacyAvailable, legacyReadFailed;
    private Workspace? legacyData;
    private string? editor, error, success;
    private Trip? editingTrip;
    private TripVariant? editingVariant;
    private string? initialFocusField;
    private int comparisonKey;
    private Func<Task>? retry;
    private bool redirectingToLogin;
    private bool IsTripList => TripId is null;
    private string ScreenTitle => IsTripList ? "Мои поездки" : IsComparison ? "Сравнение" : "Варианты поездки";
    private static string VariantsUrl(Guid id) => $"/trips/{id}/variants";
    private static string ComparisonUrl(Guid id) => $"/trips/{id}/comparison";

    protected override void OnParametersSet()
    {
        currentId = TripId;
        error = null;
        success = null;
        retry = null;
        conflict = false;
    }

    private async Task BeforeNavigate(LocationChangingContext context)
    {
        if (redirectingToLogin)
            return;
        if (busy || (editor is not null && !await Js.InvokeAsync<bool>("confirm", "Перейти на другой экран и отбросить несохранённые правки?")))
        {
            context.PreventNavigation();
            return;
        }
        if (editor is not null)
            await CloseEditor();
    }

    private void RedirectToLogin()
    {
        redirectingToLogin = true;
        Navigation.NavigateTo("/login");
    }

    protected override Task OnInitializedAsync() => Load();
    private async Task Load()
    {
        loading = true;
        readFailed = false;
        error = null;
        success = null;
        try
        {
            var loaded = await Store.ReadAsync();
            try
            {
                legacyData = await LegacyStore.ReadAsync();
                legacyAvailable = legacyData.Trips.Count > 0;
            }
            catch (Exception ex) when (ex is JSException or JsonException or InvalidOperationException)
            {
                legacyReadFailed = true;
            }
            if (loaded.Trips.Count == 0 && !legacyAvailable && IsTripList)
                loaded = await AddExampleData(loaded);
            data = loaded;
            currentId = TripId;
        }
        catch (AuthenticationRequiredException)
        {
            RedirectToLogin();
        }
        catch (Exception ex) when (ex is JSException or JsonException or InvalidOperationException or HttpRequestException)
        {
            readFailed = true;
        }
        finally { loading = false; }
    }
    private async Task<Workspace> AddExampleData(Workspace empty)
    {
        var example = ExampleData.Create();
        var trip = example.Trips.Single();
        var result = await Store.WriteAsync(example, empty.Revision, trip.Id);
        if (result == "ok")
            return example;
        if (result == "conflict")
            return await Store.ReadAsync();
        throw new InvalidOperationException("Не удалось сохранить демонстрационные данные");
    }
    private void SwitchTrip(ChangeEventArgs e)
    {
        if (Guid.TryParse(e.Value?.ToString(), out var id))
            Navigation.NavigateTo(IsComparison ? ComparisonUrl(id) : VariantsUrl(id));
    }
    private void StartEditor(string type)
    {
        editor = type;
        error = null;
        success = null;
        retry = null;
        conflict = false;
    }
    private void NewTrip()
    {
        editingTrip = null;
        StartEditor("trip");
    }
    private void EditTrip()
    {
        editingTrip = Current;
        StartEditor("trip");
    }
    private void EditListedTrip(Trip trip)
    {
        editingTrip = trip;
        StartEditor("trip");
    }
    private async Task AddExample()
    {
        if (HasExample)
            return;
        var trip = ExampleData.Create().Trips.Single();
        trip.Revision = 0;
        retry = AddExample;
        if (await Commit(next => next.Trips.Add(trip), trip.Id))
        {
            success = "Пример добавлен";
            Navigation.NavigateTo(VariantsUrl(trip.Id));
        }
    }
    private void NewVariant()
    {
        initialFocusField = null;
        editingVariant = null;
        StartEditor("variant");
    }
    private void EditVariant(Guid id)
    {
        initialFocusField = null;
        editingVariant = Current!.Variants.Single(v => v.Id == id);
        StartEditor("variant");
    }
    private void ClarifyVariant((Guid VariantId, string FieldId) item)
    {
        EditVariant(item.VariantId);
        initialFocusField = item.FieldId;
    }
    private async Task CloseEditor()
    {
        await Js.InvokeVoidAsync("tripUi.close", "editor");
        editor = null;
        error = null;
        retry = null;
    }

    private async Task<bool> Commit(Action<Workspace> change, Guid tripId, bool comparisonOnly = false)
    {
        if (busy)
            return false;
        busy = true;
        success = null;
        error = null;
        conflict = false;
        try
        {
            var next = new Workspace { SchemaVersion = data.SchemaVersion, Revision = data.Revision, Trips = [.. data.Trips] };
            var index = next.Trips.FindIndex(t => t.Id == tripId);
            if (index >= 0)
                next.Trips[index] = next.Trips[index].Copy(!comparisonOnly);
            change(next);
            next.Revision++;
            var trip = next.Trips.FirstOrDefault(t => t.Id == tripId);
            if (trip is not null)
            {
                trip.Revision++;
                trip.UpdatedAt = DateTimeOffset.UtcNow;
            }
            var result = await Store.WriteAsync(next, data.Revision, tripId, comparisonOnly);
            if (result != "ok")
            {
                conflict = result == "conflict";
                error = conflict ? "Данные изменились в другой вкладке. Черновик сохранён в форме: скопируйте нужные поля перед загрузкой актуальных данных." : "Не удалось сохранить данные на сервере";
                return false;
            }
            data = next;
            success = "Сохранено";
            retry = null;
            return true;
        }
        catch (AuthenticationRequiredException)
        {
            RedirectToLogin();
            return false;
        }
        catch (Exception ex) when (ex is JSException or JsonException or InvalidOperationException or HttpRequestException)
        {
            error = "Не удалось сохранить данные на сервере";
            return false;
        }
        finally { busy = false; }
    }
    private async Task SaveTrip(TripDraft draft)
    {
        if (draft.Validate().Count > 0)
            return;
        var created = editingTrip is null;
        var id = editingTrip?.Id ?? Guid.NewGuid();
        retry = () => SaveTrip(draft);
        if (await Commit(next =>
        {
            var trip = next.Trips.FirstOrDefault(t => t.Id == id);
            if (trip is null)
            {
                trip = new Trip { Id = id };
                next.Trips.Add(trip);
            }
            draft.Apply(trip);
        }, id))
        {
            await CloseEditor();
            if (created)
            {
                Navigation.NavigateTo(VariantsUrl(id));
                await Analytics.TripCreatedAsync();
            }
        }
    }
    private async Task SaveVariant(VariantDraft draft)
    {
        if (draft.Validate().Count > 0)
            return;
        var tripId = Current!.Id;
        var created = editingVariant is null;
        var variantId = editingVariant?.Id ?? Guid.NewGuid();
        retry = () => SaveVariant(draft);
        if (await Commit(next =>
        {
            var trip = next.Trips.Single(t => t.Id == tripId);
            var variant = trip.Variants.FirstOrDefault(v => v.Id == variantId);
            if (variant is null)
            {
                variant = new TripVariant { Id = variantId, TripId = tripId };
                trip.Variants.Add(variant);
            }
            draft.Apply(variant);
        }, tripId))
        {
            await CloseEditor();
            if (created && IsComparison)
                Navigation.NavigateTo(VariantsUrl(tripId));
        }
    }
    private async Task DeleteVariant(Guid id)
    {
        var name = Current!.Variants.Single(v => v.Id == id).Name;
        if (!await Js.InvokeAsync<bool>("confirm", $"Удалить вариант «{name}»?"))
            return;
        await ConfirmedDelete(id, Current.Id);
    }
    private async Task ConfirmedDelete(Guid id, Guid tripId)
    {
        retry = () => ConfirmedDelete(id, tripId);
        await Commit(next =>
        {
            var trip = next.Trips.Single(t => t.Id == tripId);
            trip.Variants.RemoveAll(v => v.Id == id);
            trip.SelectedVariantIds.Remove(id);
        }, tripId);
    }
    private async Task ToggleVariant(Guid id)
    {
        var tripId = Current!.Id;
        retry = () => ToggleVariant(id);
        if (!await Commit(next =>
        {
            var selected = next.Trips.Single(t => t.Id == tripId).SelectedVariantIds;
            if (!selected.Remove(id))
                selected.Add(id);
        }, tripId, comparisonOnly: true))
            comparisonKey++; // Recreate checkbox DOM after a rejected write.
    }
    private async Task ToggleCriterion(string key)
    {
        var tripId = Current!.Id;
        retry = () => ToggleCriterion(key);
        if (!await Commit(next =>
        {
            var selected = next.Trips.Single(t => t.Id == tripId).SelectedCriteria;
            if (!selected.Remove(key))
                selected.Add(key);
        }, tripId, comparisonOnly: true))
            comparisonKey++;
    }
    private Task Retry() => retry?.Invoke() ?? Task.CompletedTask;
    private async Task ImportLegacy()
    {
        if (legacyData is null || busy || !await Js.InvokeAsync<bool>("confirm", "Импортировать локальные поездки в вашу учётную запись? Локальная копия останется в браузере."))
            return;
        busy = true;
        error = null;
        try
        {
            foreach (var source in legacyData.Trips)
            {
                if (data.Trips.Any(x => x.Id == source.Id))
                    continue;
                var next = data.Copy();
                next.Trips.Add(source.Copy());
                var result = await Store.WriteAsync(next, data.Revision, source.Id);
                if (result != "ok")
                    throw new InvalidOperationException("Импорт остановлен. Уже импортированные поездки сохранены, локальная копия не удалена.");
                data = next;
            }
            legacyAvailable = false;
            success = "Локальные поездки импортированы. Исходная копия оставлена в браузере.";
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            error = ex.Message;
        }
        finally { busy = false; }
    }
    private async Task Logout()
    {
        try
        {
            await Auth.LogoutAsync();
        }
        finally { Navigation.NavigateTo("/login", forceLoad: true); }
    }
    private async Task ReloadCurrent()
    {
        if (editor is not null && !await Js.InvokeAsync<bool>("confirm", "Загрузить актуальные данные и отбросить несохранённые правки?"))
            return;
        // Keep the draft visible if reading still fails.
        try
        {
            var loaded = await Store.ReadAsync();
            if (editor is not null)
                await CloseEditor();
            data = loaded;
            error = null;
            success = null;
            conflict = false;
            retry = null;
            currentId = TripId;
        }
        catch (AuthenticationRequiredException)
        {
            RedirectToLogin();
        }
        catch (Exception ex) when (ex is JSException or JsonException or InvalidOperationException or HttpRequestException)
        {
            error = "Не удалось прочитать актуальные данные. Правки остаются в форме.";
        }
    }
}
