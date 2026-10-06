namespace FurnitureStore.Web.ViewModels.Sales;

/// <summary>
/// Province → ward / commune pickers (Views/Shared/_AddressPicker.cshtml) posting "Command.Province" and "Command.Ward",
/// used by the checkout and the address book. Wards of another province are loaded by address-picker.js.
/// </summary>
/// <param name="ColumnClass">Bootstrap column of each picker.</param>
public sealed record AddressPickerModel(string? Province, string? Ward, string ColumnClass = "col-md-6", bool SmallLabels = false);
