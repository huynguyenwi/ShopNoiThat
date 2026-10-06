using FluentValidation.TestHelper;
using FurnitureStore.Application.Sales;

namespace FurnitureStore.Tests.Sales;

/// <summary>Two-level addresses since 07/2025: 34 provinces → 3,321 wards / communes, no district.</summary>
public sealed class VietnamProvincesTests
{
    [Fact]
    public void Data_HasThe34ProvincesAndAll3321Wards()
    {
        Assert.Equal(34, VietnamProvinces.Provinces.Count);
        Assert.Equal(3321, VietnamProvinces.Provinces.Sum(p => p.Wards.Count));
        Assert.Equal(["TP. Hà Nội", "TP. Hồ Chí Minh"], VietnamProvinces.All.Take(2));   // cities first, short "TP." names
        Assert.Equal(VietnamProvinces.Provinces.Count, VietnamProvinces.Provinces.Select(p => p.Code).Distinct().Count());
        Assert.All(VietnamProvinces.Provinces, p => Assert.NotEmpty(p.Wards));
        Assert.All(VietnamProvinces.Provinces.SelectMany(p => p.Wards), w => Assert.StartsWith(w.Type + " ", w.Name));
    }

    [Fact]
    public void Wards_AreGroupedByType_ThenSortedTheVietnameseWay()
    {
        var hcm = VietnamProvinces.Find("TP. Hồ Chí Minh")!;
        Assert.Equal(79, hcm.Code);
        Assert.Equal(["Phường", "Xã", "Đặc khu"], hcm.Wards.Select(w => w.Type).Distinct());

        var phuong = hcm.Wards.Where(w => w.Type == "Phường").Select(w => w.ShortName).ToList();
        var bIndex = phuong.FindIndex(n => n.StartsWith('B'));
        var dStrokeIndex = phuong.FindIndex(n => n.StartsWith('Đ'));
        var eIndex = phuong.FindIndex(n => n.StartsWith('G'));
        Assert.True(bIndex < dStrokeIndex && dStrokeIndex < eIndex, "Đ sorts after D, before G (not after Z)");
        Assert.Equal("Sài Gòn", hcm.Wards.Single(w => w.Name == "Phường Sài Gòn").Label);
    }

    [Fact]
    public void ALabel_IsTheFullName_WhenAWardAndACommuneShareAName()
    {
        foreach (var province in VietnamProvinces.Provinces)
        {
            foreach (var group in province.Wards.GroupBy(w => w.Label, StringComparer.OrdinalIgnoreCase))
            {
                Assert.True(group.Count() == 1, $"{province.Name}: two entries labelled \"{group.Key}\"");
            }
        }
    }

    [Theory]
    [InlineData("TP. Hồ Chí Minh", "Phường Sài Gòn", true)]
    [InlineData("tp. hồ chí minh", "  phường  sài gòn ", true)]   // case and spacing do not matter
    [InlineData("TP. Hà Nội", "Phường Sài Gòn", false)]           // ward of another province
    [InlineData("TP. Hồ Chí Minh", "Phường Bến Nghé", false)]     // merged into Phường Sài Gòn in 07/2025
    [InlineData("Tỉnh không có", "Phường Sài Gòn", false)]
    [InlineData("TP. Hồ Chí Minh", "", false)]
    public void IsValidWard_ChecksTheWardBelongsToTheProvince(string province, string ward, bool expected)
    {
        Assert.Equal(expected, VietnamProvinces.IsValidWard(province, ward));
    }

    [Fact]
    public void Checkout_RequiresAWardOfTheChosenProvince()
    {
        var validator = new CheckoutCommandValidator();
        CheckoutCommand Command(string province, string ward) => new()
        {
            FullName = "A", Phone = "0912345678", Email = "a@example.com", AddressLine = "1 Lê Lợi", Province = province, Ward = ward
        };

        validator.TestValidate(Command("TP. Hồ Chí Minh", "Phường Sài Gòn")).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(Command("TP. Hồ Chí Minh", "")).ShouldHaveValidationErrorFor(c => c.Ward).WithErrorMessage("Vui lòng chọn phường / xã.");
        validator.TestValidate(Command("TP. Hà Nội", "Phường Sài Gòn")).ShouldHaveValidationErrorFor(c => c.Ward)
            .WithErrorMessage("Phường / xã này không thuộc TP. Hà Nội. Vui lòng chọn lại.");

        var noProvince = validator.TestValidate(Command("", "Phường Sài Gòn"));
        noProvince.ShouldHaveValidationErrorFor(c => c.Province);
        noProvince.ShouldNotHaveValidationErrorFor(c => c.Ward);   // one message at a time: choose the province first
    }

    [Fact]
    public void AddressBook_UsesTheSameRules()
    {
        var result = new CustomerAddressCommandValidator().TestValidate(new CustomerAddressCommand
        {
            RecipientName = "A", Phone = "0912345678", AddressLine = "1 Lê Lợi", Province = "Khánh Hòa", Ward = "Phường Sài Gòn"
        });

        result.ShouldHaveValidationErrorFor(c => c.Ward);
    }
}
