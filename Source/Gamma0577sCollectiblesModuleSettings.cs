namespace Celeste.Mod.Gamma0577sCollectibles;

public class Gamma0577sCollectiblesModuleSettings : EverestModuleSettings {
	public bool StrawberryToggle { get; set; } = false;

	public bool GoldenStrawberryToggle { get; set; } = false;

	public enum StrawberryCounterVisibility {
		Never,
		Collection,
		Always,
	}

	public StrawberryCounterVisibility StrawberryCounterToggle { get; set; } = StrawberryCounterVisibility.Never;

	public enum StrawberryTrackerVisibility {
		Never,
		Default,
		Always,
		AlwaysAlways,
	}

	public StrawberryTrackerVisibility StrawberryTrackerToggle { get; set; } = StrawberryTrackerVisibility.Default;

	public bool CassetteToggle { get; set; } = false;

	public bool HeartGemToggle { get; set; } = false;

	public bool SummitGemToggle { get; set; } = false;
}
