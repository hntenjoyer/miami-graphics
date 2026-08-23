using MiamiGraphics.Core.Services;
using Xunit;
using static MiamiGraphics.Core.Services.InteriorClutterDetector;

namespace MiamiGraphics.Core.Tests;

public class InteriorClutterDetectorTests
{
    private const int Vanilla = 427;

    private static Finding F(int objects, bool sameAsVanilla = false, int vanilla = Vanilla)
        => new("v_int_10.ytyp", "update.rpf", objects, vanilla, sameAsVanilla);

    [Fact]
    public void Без_перекрытий_интерьеры_ванильные()
    {
        var r = Classify(Array.Empty<Finding>());
        Assert.Equal(State.Vanilla, r.State);
        Assert.NotNull(r.Note);
    }

    [Fact]
    public void Перекрытие_ванильным_файлом_это_всё_ещё_ваниль()
    {
        var r = Classify(new[] { F(Vanilla, sameAsVanilla: true) });
        Assert.Equal(State.Vanilla, r.State);
    }

    [Theory]
    [InlineData(186)]
    [InlineData(174)]
    [InlineData(157)]
    public void Урезанный_список_объектов_это_вырезанный_мусор(int objects)
    {
        Assert.Equal(State.Stripped, Classify(new[] { F(objects) }).State);
    }

    [Fact]
    public void Чужой_файл_с_теми_же_объектами_это_кастомный_интерьер()
    {
        var r = Classify(new[] { F(Vanilla, sameAsVanilla: false) });
        Assert.Equal(State.Custom, r.State);
    }

    [Fact]
    public void Потеря_пары_объектов_за_вырезание_не_сходит()
    {
        Assert.Equal(State.Custom, Classify(new[] { F(425) }).State);
    }

    [Fact]
    public void Ровно_на_пороге_ещё_не_вырезано()
    {
        Assert.Equal(State.Custom, Classify(new[] { F(385) }).State);
        Assert.Equal(State.Stripped, Classify(new[] { F(384) }).State);
    }

    [Fact]
    public void Одного_вычищенного_зала_достаточно()
    {
        var r = Classify(new[] { F(Vanilla, sameAsVanilla: true), F(157) });
        Assert.Equal(State.Stripped, r.State);
    }

    [Fact]
    public void Все_залы_чужие_но_полные_это_кастомный()
    {
        var r = Classify(new[] { F(Vanilla), F(Vanilla), F(Vanilla) });
        Assert.Equal(State.Custom, r.State);
    }

    [Fact]
    public void Нулевая_ваниль_не_объявляет_вырезанным()
    {
        var r = Classify(new[] { F(0, sameAsVanilla: false, vanilla: 0) });
        Assert.Equal(State.Custom, r.State);
    }

    [Fact]
    public void Мусор_вместо_ytyp_не_роняет_разбор()
    {
        Assert.Equal(-1, CountObjects(new byte[] { 1, 2, 3, 4, 5 }));
        Assert.Equal(-1, CountObjects(Array.Empty<byte>()));
    }

    [Fact]
    public void Чужая_модель_при_ванильном_списке_объектов_это_кастомный()
    {
        var model = new Finding("v_10_liquorstore.ydr", "update.rpf", 0, 0, SameAsVanilla: false);
        var r = Classify(new[] { model });
        Assert.Equal(State.Custom, r.State);
    }

    [Fact]
    public void Чужая_модель_не_перебивает_вырезанный_список()
    {
        var model = new Finding("v_10_liquorstore.ydr", "update.rpf", 0, 0, SameAsVanilla: false);
        Assert.Equal(State.Stripped, Classify(new[] { model, F(157) }).State);
    }
}
