using System.Collections.Generic;
using System.Collections.ObjectModel;
using MpcConverter.Core.Model;
using Xunit;

namespace MpcConverter.Core.Tests;

public class TrackOptionsSyncTests
{
    [Fact]
    public void Sync_AddsManuallyTypedNames_Sorted()
    {
        var options = new ObservableCollection<string>();
        TrackOptionsSync.Sync(options, new[] { "Synth", "Bass", "Drums" });
        Assert.Equal(new[] { "Bass", "Drums", "Synth" }, options);
    }

    [Fact]
    public void Sync_SkipSentinelAndBlanks_Excluded()
    {
        var options = new ObservableCollection<string>();
        TrackOptionsSync.Sync(options, new[] { "Bass", "(skip)", "", null, "  " });
        Assert.Equal(new[] { "Bass" }, options);
    }

    [Fact]
    public void Sync_IsCaseInsensitiveDedup()
    {
        var options = new ObservableCollection<string>();
        TrackOptionsSync.Sync(options, new[] { "Bass", "bass", "BASS" });
        Assert.Single(options);
    }

    [Fact]
    public void Sync_DoesNotRemoveOrReplaceAStillUsedName()
    {
        // The core fix: an in-use name must survive a sync (no destructive Clear),
        // so the ComboBox showing it is never reset.
        var options = new ObservableCollection<string> { "Bass" };
        var theInstance = options[0];

        // A new row adds "Drums"; "Bass" is still used by another row.
        TrackOptionsSync.Sync(options, new[] { "Bass", "Drums" });

        Assert.Contains("Bass", options);
        // "Bass" was neither removed nor re-created — same object reference.
        Assert.Same(theInstance, options[options.IndexOf("Bass")]);
    }

    [Fact]
    public void Sync_RemovesNamesNoLongerUsed()
    {
        var options = new ObservableCollection<string> { "Bass", "Drums" };
        TrackOptionsSync.Sync(options, new[] { "Bass" }); // Drums no longer assigned
        Assert.Equal(new[] { "Bass" }, options);
    }

    [Fact]
    public void Sync_TypingProgressively_ConvergesWithoutClobbering()
    {
        // Simulate committing a new name on one row while another keeps "Kick".
        var options = new ObservableCollection<string> { "Kick" };
        var kick = options[0];
        TrackOptionsSync.Sync(options, new[] { "Kick", "Hats" });
        Assert.Equal(new[] { "Hats", "Kick" }, options);
        Assert.Same(kick, options[options.IndexOf("Kick")]);
    }
}
