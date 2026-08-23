using MiamiGraphics.Core.Injector;
using Xunit;

namespace MiamiGraphics.Core.Tests;

public class ContentXmlRepairTests
{
    private const string Xml = """
<?xml version="1.0" encoding="UTF-8"?>
<CDataFileMgr__ContentsOfDataFileXml>
  <dataFiles>
    <Item>
        <filename>update:/gucci_component/gucci_guns.rpf</filename>
        <fileType>RPF_FILE</fileType>
        <locked value="true"/>
    </Item>
    <Item>
        <filename>update:/car_logos/miami_cars.rpf</filename>
        <fileType>RPF_FILE</fileType>
        <locked value="true"/>
    </Item>
  </dataFiles>
  <contentChangeSets>
    <Item>
      <changeSetName>CCS_TITLE_UPDATE_STREAMING</changeSetName>
      <filesToEnable>
        <Item>update:/gucci_component/gucci_guns.rpf</Item>
        <Item>update:/car_logos/miami_cars.rpf</Item>
      </filesToEnable>
    </Item>
  </contentChangeSets>
</CDataFileMgr__ContentsOfDataFileXml>
""";

    [Fact]
    public void Снимает_и_строку_включения_и_блок_объявления()
    {
        var result = ContentXmlRepair.RemoveDeclarations(
            Xml, new[] { "gucci_component/gucci_guns.rpf" }, out var removed);

        Assert.Equal(new[] { "gucci_component/gucci_guns.rpf" }, removed);
        Assert.DoesNotContain("gucci_guns.rpf", result);
    }

    [Fact]
    public void Чужие_объявления_остаются_нетронутыми()
    {
        var result = ContentXmlRepair.RemoveDeclarations(
            Xml, new[] { "gucci_component/gucci_guns.rpf" }, out _);

        Assert.Contains("<filename>update:/car_logos/miami_cars.rpf</filename>", result);
        Assert.Contains("<Item>update:/car_logos/miami_cars.rpf</Item>", result);
        Assert.Contains("CCS_TITLE_UPDATE_STREAMING", result);
        Assert.Contains("</dataFiles>", result);
    }

    [Fact]
    public void Соседний_блок_не_разрезан()
    {
        var result = ContentXmlRepair.RemoveDeclarations(
            Xml, new[] { "gucci_component/gucci_guns.rpf" }, out _);

        int items = result.Split("<Item>").Length - 1;
        int closes = result.Split("</Item>").Length - 1;
        Assert.Equal(items, closes);
    }

    [Fact]
    public void Ничего_не_трогаем_если_путь_не_объявлен()
    {
        var result = ContentXmlRepair.RemoveDeclarations(
            Xml, new[] { "nothing/here.rpf" }, out var removed);

        Assert.Empty(removed);
        Assert.Equal(Xml, result);
    }

    [Fact]
    public void Отступ_соседнего_тега_не_страдает()
    {
        const string lf = "\n";
        var xml = "<root>" + lf
                + "\t<dataFiles>" + lf
                + "\t\t<Item>" + lf
                + "\t\t\t<filename>update:/dead/gone.rpf</filename>" + lf
                + "\t\t</Item>" + lf
                + "\t</dataFiles>" + lf
                + "</root>";
        var expected = "<root>" + lf + "\t<dataFiles>" + lf + "\t</dataFiles>" + lf + "</root>";

        var result = ContentXmlRepair.RemoveDeclarations(xml, new[] { "dead/gone.rpf" }, out var removed);

        Assert.Single(removed);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Несколько_путей_за_один_проход()
    {
        var result = ContentXmlRepair.RemoveDeclarations(
            Xml, new[] { "gucci_component/gucci_guns.rpf", "car_logos/miami_cars.rpf" }, out var removed);

        Assert.Equal(2, removed.Count);
        Assert.DoesNotContain("update:/", result);
        Assert.Contains("<filesToEnable>", result);
    }
}
