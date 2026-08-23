using MiamiGraphics.Core.Services;
using Xunit;

namespace MiamiGraphics.Core.Tests;

public class InteriorClutterStripperTests
{
    [Fact]
    public void Remap_shifts_survivors_and_drops_removed()
    {
        var map = new[] { -1, 0, -1, 1 };
        var result = InteriorClutterStripper.RemapAttachments(new uint[] { 0, 1, 2, 3 }, map);
        Assert.Equal(new uint[] { 0, 1 }, result);
    }

    [Fact]
    public void Remap_keeps_order_of_survivors()
    {
        var map = new[] { 0, -1, 1, 2 };
        var result = InteriorClutterStripper.RemapAttachments(new uint[] { 3, 0, 2 }, map);
        Assert.Equal(new uint[] { 2, 0, 1 }, result);
    }

    [Fact]
    public void Remap_drops_indexes_past_the_end_of_the_old_array()
    {
        var map = new[] { 0, 1 };
        var result = InteriorClutterStripper.RemapAttachments(new uint[] { 1, 7 }, map);
        Assert.Equal(new uint[] { 1 }, result);
    }

    [Fact]
    public void Remap_of_nothing_is_empty()
    {
        Assert.Empty(InteriorClutterStripper.RemapAttachments(null, new[] { 0 }));
        Assert.Empty(InteriorClutterStripper.RemapAttachments(global::System.Array.Empty<uint>(), new[] { 0 }));
    }

    [Fact]
    public void Remap_of_all_removed_leaves_no_attachments()
    {
        var map = new[] { -1, -1 };
        Assert.Empty(InteriorClutterStripper.RemapAttachments(new uint[] { 0, 1 }, map));
    }
}
