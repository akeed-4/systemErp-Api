using ERP.Service.Services.CarShowroom;

namespace ERP.Tests;

/// <summary>اختبارات وحدة صِرفة لتوزيع المصروف على السيارات (بلا قاعدة بيانات).</summary>
public class ChargeAllocatorTests
{
    [Fact]
    public void Equal_split_puts_the_rounding_difference_on_the_last_vehicle()
    {
        Assert.Equal(new[] { 33.33m, 33.33m, 33.34m }, ChargeAllocator.Equal(100m, 3));
        Assert.Equal(new[] { 0.33m, 0.33m, 0.34m }, ChargeAllocator.Equal(1m, 3));
        Assert.Equal(new[] { 1500m, 1500m }, ChargeAllocator.Equal(3000m, 2));
    }

    [Fact]
    public void Weighted_split_follows_the_weights_and_always_sums_to_the_total()
    {
        Assert.Equal(new[] { 600m, 300m }, ChargeAllocator.Allocate(900m, new[] { 60000m, 30000m }));
        // 1000 على (1، 1، 1) لسبع سيارات: 142.86 × 6 والباقي 142.84 للأخيرة
        var seven = ChargeAllocator.Allocate(1000m, Enumerable.Repeat(1m, 7).ToArray());
        Assert.Equal(1000m, seven.Sum());
        Assert.All(seven.Take(6), s => Assert.Equal(142.86m, s));
        Assert.Equal(142.84m, seven[6]);
    }

    [Theory]
    [InlineData(1234.57, 3)]
    [InlineData(0.05, 7)]
    [InlineData(99999.99, 13)]
    public void Shares_are_never_negative_and_sum_exactly(double total, int count)
    {
        var amount = (decimal)total;
        var weights = Enumerable.Range(1, count).Select(i => (decimal)(i * i)).ToArray();
        foreach (var shares in new[] { ChargeAllocator.Equal(amount, count), ChargeAllocator.Allocate(amount, weights) })
        {
            Assert.Equal(amount, shares.Sum());
            Assert.All(shares, s => Assert.True(s >= 0));
            Assert.All(shares, s => Assert.Equal(Math.Round(s, 2), s));
        }
    }

    [Fact]
    public void Rounding_up_early_shares_never_pushes_the_last_vehicle_below_zero()
    {
        // 0.01 على وزنين متساويين وثالث صفري: التقريب يعطي الأولى 0.01 ولا يبقى للباقي شيء
        Assert.Equal(new[] { 0.01m, 0m, 0m }, ChargeAllocator.Allocate(0.01m, new[] { 1m, 1m, 0m }));
    }

    [Fact]
    public void A_zero_weight_vehicle_gets_nothing_and_a_single_vehicle_gets_everything()
    {
        Assert.Equal(new[] { 0m, 500m }, ChargeAllocator.Allocate(500m, new[] { 0m, 10m }));
        Assert.Equal(new[] { 777.77m }, ChargeAllocator.Allocate(777.77m, new[] { 5m }));
        Assert.Empty(ChargeAllocator.Allocate(100m, Array.Empty<decimal>()));
    }

    [Fact]
    public void Invalid_weights_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => ChargeAllocator.Allocate(100m, new[] { 0m, 0m }));
        Assert.Throws<ArgumentException>(() => ChargeAllocator.Allocate(100m, new[] { 5m, -1m }));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChargeAllocator.Allocate(-1m, new[] { 1m }));
    }
}
