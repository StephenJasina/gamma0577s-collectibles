using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.InlineRT;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using System;
using System.Reflection;

namespace Celeste.Mod.Gamma0577sCollectibles;

public class Gamma0577sCollectiblesModule : EverestModule {
	public static Gamma0577sCollectiblesModule Instance { get; private set; }

	public override Type SettingsType => typeof(Gamma0577sCollectiblesModuleSettings);
	public static Gamma0577sCollectiblesModuleSettings Settings => (Gamma0577sCollectiblesModuleSettings) Instance._Settings;

	public override Type SessionType => typeof(Gamma0577sCollectiblesModuleSession);
	public static Gamma0577sCollectiblesModuleSession Session => (Gamma0577sCollectiblesModuleSession) Instance._Session;

	public override Type SaveDataType => typeof(Gamma0577sCollectiblesModuleSaveData);
	public static Gamma0577sCollectiblesModuleSaveData SaveData => (Gamma0577sCollectiblesModuleSaveData) Instance._SaveData;

	public Gamma0577sCollectiblesModule() {
		Instance = this;
#if DEBUG
		// debug builds use verbose logging
		Logger.SetLogLevel(nameof(Gamma0577sCollectiblesModule), LogLevel.Verbose);
#else
		// release builds use info logging to reduce spam in log files
		Logger.SetLogLevel(nameof(Gamma0577sCollectiblesModule), LogLevel.Info);
#endif
	}

	private static ILHook strawberryCollectRoutineHook;
	private static ILHook strawberryOrigUpdateHook;
	private static ILHook levelOrigLoadLevelHook;
	private static ILHook strawberryHooksReplaceStrawberrySpriteHook;
	private static ILHook silverBerryAddedHook;
	private static ILHook abstractMiniHeartAddedHook;

	public override void Load() {
		// Strawberries. This is done somewhat aggressively to guard
		// against cases where isGhostBerry is modified.
		IL.Celeste.Strawberry.ctor += ModStrawberry;
		IL.Celeste.Strawberry.Added += ModStrawberry;
		IL.Celeste.Strawberry.OnPlayer += ModStrawberry;
		strawberryCollectRoutineHook = new ILHook(
			typeof(Strawberry).GetMethod("CollectRoutine", BindingFlags.NonPublic | BindingFlags.Instance).GetStateMachineTarget(),
			ModStrawberry
		);
		strawberryOrigUpdateHook = new ILHook(
			typeof(Strawberry).GetMethod("orig_Update", BindingFlags.Public | BindingFlags.Instance),
			ModStrawberry
		);
		// We also modify the methods that check the save data in order
		// to account for entities that use them. This includes, for
		// example, CollabUtils2's silver berries.
		// IL.Celeste.SaveData.CheckStrawberry_AreaKey_EntityID += ModCheckStrawberry;
		// IL.Celeste.SaveData.CheckStrawberry_EntityID += ModCheckStrawberry;

		// Golden strawberries
		levelOrigLoadLevelHook = new ILHook(
			typeof(Level).GetMethod("orig_LoadLevel", BindingFlags.Public | BindingFlags.Instance),
			ModGoldenStrawberry
		);

		// Strawberry display in the top left corner
		IL.Celeste.TotalStrawberriesDisplay.Update += ModStrawberryDisplay;

		// Strawberry tracker at the bottom of the pause menu
		IL.Celeste.GameplayStats.Render += ModStrawberryTracker;

		// Cassettes
		IL.Celeste.Cassette.Added += ModCassette;

		// Hearts
		IL.Celeste.HeartGem.Awake += ModCrystalHeart;

		// Summit gems
		IL.Celeste.SummitGem.ctor += ModSummitGem;

		// Collab Utils 2 entities
		EverestModuleMetadata collabUtils2 = new() {
			Name = "CollabUtils2",
			Version = new Version(1, 13 ,0)
		};
		if (Everest.Loader.TryGetDependency(collabUtils2, out EverestModule collabUtils2Module)) {
			Assembly collabUtils2Assembly = collabUtils2Module.GetType().Assembly;

			Type strawberryHooks = collabUtils2Assembly.GetType("Celeste.Mod.CollabUtils2.Entities.StrawberryHooks");
			strawberryHooksReplaceStrawberrySpriteHook = new ILHook(
				strawberryHooks.GetMethod("replaceStrawberrySprite", BindingFlags.NonPublic | BindingFlags.Static),
				ModStrawberry
			);

			Type silverBerry = collabUtils2Assembly.GetType("Celeste.Mod.CollabUtils2.Entities.SilverBerry");
			silverBerryAddedHook = new ILHook(
				silverBerry.GetMethod("Added", BindingFlags.Public | BindingFlags.Instance),
				ModSilverBerry
			);

			Type abstractMiniHeart = collabUtils2Assembly.GetType("Celeste.Mod.CollabUtils2.Entities.AbstractMiniHeart");
			abstractMiniHeartAddedHook = new ILHook(
				abstractMiniHeart.GetMethod("Added", BindingFlags.Public | BindingFlags.Instance),
				ModCrystalHeart
			);
		}
	}

	public override void Unload() {
		// Strawberries
		IL.Celeste.Strawberry.ctor -= ModStrawberry;
		IL.Celeste.Strawberry.Added -= ModStrawberry;
		IL.Celeste.Strawberry.OnPlayer -= ModStrawberry;
		strawberryCollectRoutineHook.Dispose();
		strawberryOrigUpdateHook.Dispose();

		// Golden strawberries
		levelOrigLoadLevelHook.Dispose();

		// Strawberry display in the top left corner
		IL.Celeste.TotalStrawberriesDisplay.Update -= ModStrawberryDisplay;

		// Strawberry tracker at the bottom of the pause menu
		IL.Celeste.GameplayStats.Render -= ModStrawberryTracker;

		// Cassettes
		IL.Celeste.Cassette.Added -= ModCassette;

		// Hearts
		IL.Celeste.HeartGem.Awake -= ModCrystalHeart;

		// Summit gems
		IL.Celeste.SummitGem.ctor -= ModSummitGem;

		// Collab Utils 2 entities
		strawberryHooksReplaceStrawberrySpriteHook?.Dispose();
		silverBerryAddedHook?.Dispose();
		abstractMiniHeartAddedHook?.Dispose();
	}

	private static void ModStrawberry(ILContext il) {
		ILCursor cursor = new(il);
		while (cursor.TryGotoNext(MoveType.After, instr => instr.MatchLdfld<Strawberry>("isGhostBerry") || instr.MatchCallvirt<SaveData>("CheckStrawberry"))) {
			cursor.EmitDelegate<Func<bool>>(IsDisabledStrawberries);
			cursor.EmitAnd();
		}
	}

	// private static void ModCheckStrawberry(ILContext il) {
	// 	ILCursor cursor = new(il);
	// 	while (cursor.TryGotoNext(MoveType.Before, instr => instr.MatchRet()))
	// 	{
	// 		cursor.EmitDelegate<Func<bool>>(IsDisabledStrawberries);
	// 		cursor.EmitAnd();
	// 		// TODO: Make this more elegant
	// 		cursor.GotoNext(MoveType.Before, instr => instr.MatchRet());
	// 	}
	// }

	private static bool IsDisabledStrawberries() {
		return !Settings.StrawberryToggle;
	}

	private static void ModGoldenStrawberry(ILContext il) {
		ILCursor cursor = new(il);
		while (cursor.TryGotoNext(MoveType.After, instr => instr.MatchLdfld<AreaModeStats>("Completed"))) {
			cursor.EmitDelegate<Func<bool>>(IsEnabledGoldenStrawberries);
			cursor.EmitOr();
		}

		cursor = new(il);
		while (cursor.TryGotoNext(MoveType.After, instr => instr.MatchCallvirt<SaveData>("get_UnlockedModes"))) {
			ILCursor cursorIf = new(cursor);
			ILLabel labelfi = cursor.MarkLabel();
			cursorIf.EmitDelegate<Func<bool>>(IsEnabledGoldenStrawberries);
			cursorIf.EmitBrfalse(labelfi);
			cursorIf.EmitPop();
			cursorIf.EmitLdcI4(3);
		}
	}

	private static void ModSilverBerry(ILContext il) {
		ILCursor cursor = new(il);
		EverestModuleMetadata collabUtils2 = new() {
			Name = "CollabUtils2",
			Version = new Version(1, 13 ,0)
		};
		if (Everest.Loader.TryGetDependency(collabUtils2, out EverestModule collabUtils2Module)) {
			Assembly collabUtils2Assembly = collabUtils2Module.GetType().Assembly;
			Type silverBerry = collabUtils2Assembly.GetType("Celeste.Mod.CollabUtils2.Entities.SilverBerry");
			while (cursor.TryGotoNext(MoveType.After, instr => instr.MatchLdfld(silverBerry, "alwaysSpawn"))) {
				cursor.EmitDelegate<Func<bool>>(IsEnabledGoldenStrawberries);
				cursor.EmitOr();
			}
		}
	}

	private static bool IsEnabledGoldenStrawberries() {
		return Settings.GoldenStrawberryToggle;
	}

	private static void ModStrawberryDisplay(ILContext il) {
		ILCursor cursor = new(il);
		while (cursor.TryGotoNext(MoveType.Before, instr => instr.MatchStfld<TotalStrawberriesDisplay>("DrawLerp"))) {
			cursor.EmitLdloc0();
			cursor.EmitDelegate<Func<Level, float>>(StrawberryDisplayLerpMutlipier);
			cursor.EmitMul();
			// TODO: Make this more elegant
			cursor.GotoNext(MoveType.Before, instr => instr.MatchStfld<TotalStrawberriesDisplay>("DrawLerp"));
		}
	}

	private static float StrawberryDisplayLerpMutlipier(Level level) {
		// We control the TotalStrawberriesDisplay by changing its
		// DrawLerp field
		return (
			Settings.StrawberryCounterToggle == Gamma0577sCollectiblesModuleSettings.StrawberryCounterVisibility.Always
			|| (Settings.StrawberryCounterToggle == Gamma0577sCollectiblesModuleSettings.StrawberryCounterVisibility.Collection && !(level.Paused && level.PauseMainMenuOpen))
		) ? 0f : 1f;
	}

	private static void ModStrawberryTracker(ILContext il) {
		ILCursor cursor = new(il);

		// First, find the labels used for branching. One is a
		// continuation towards the next phase of checks; the other just
		// returns immediately without rendering
		ILLabel labelContinue = null;
		ILLabel labelReturn = null;
		if (cursor.TryGotoNext(MoveType.After, instr => instr.MatchLdfld<AreaModeStats>("Completed"))) {
			cursor.GotoNext(MoveType.After, instr => instr.MatchBrtrue(out labelContinue));
			cursor.GotoNext(MoveType.After, instr => instr.MatchBrfalse(out labelReturn));
		} else {
			// Failsafe in case the search failed
			return;
		}

		// Now circumvent the first check with our custom check
		cursor = new(il);
		cursor.GotoNext(MoveType.Before, instr => instr.MatchLdfld<AreaModeStats>("Completed"));
		cursor.GotoNext(MoveType.Before, instr => instr.MatchBrtrue(out ILLabel label));
		cursor.EmitPop();
		cursor.EmitLdsfld(typeof(SaveData).GetField("Instance", BindingFlags.Public | BindingFlags.Static));
		cursor.EmitLdloc3();
		cursor.EmitLdloc2();
		cursor.EmitDelegate<Func<SaveData, AreaModeStats, AreaKey, bool>>(IsEnabledStrawberryTracker);
		cursor.EmitBrtrue(labelContinue);
		cursor.EmitBr(labelReturn);

		// Finally, circumvent the second check
		while (cursor.TryGotoNext(MoveType.Before, instr => instr.MatchBle(out ILLabel label) && label.Target == labelReturn.Target)) {
			// First, circumvent the branch
			cursor.EmitPop();
			cursor.EmitLdcI4(-1);

			// Now, substitute it with our own
			cursor.GotoNext(MoveType.After, instr => instr.MatchBle(out ILLabel label) && label.Target == labelReturn.Target);
			cursor.EmitLdsfld(typeof(SaveData).GetField("Instance", BindingFlags.Public | BindingFlags.Static));
			cursor.EmitLdloc3();
			cursor.EmitLdloc2();
			cursor.EmitDelegate<Func<SaveData, AreaModeStats, AreaKey, bool>>(IsEnabledStrawberryTracker);
			cursor.EmitBrfalse(labelReturn);
		}
	}

	private static bool IsEnabledStrawberryTracker(SaveData saveData, AreaModeStats areaModeStats, AreaKey area) {
		ModeProperties modeProperties = AreaData.Get(area).Mode[(int)area.Mode];
		bool toReturn = Settings.StrawberryTrackerToggle switch {
			Gamma0577sCollectiblesModuleSettings.StrawberryTrackerVisibility.Never => false,
			Gamma0577sCollectiblesModuleSettings.StrawberryTrackerVisibility.Always => modeProperties.TotalStrawberries > 0,
			Gamma0577sCollectiblesModuleSettings.StrawberryTrackerVisibility.AlwaysAlways => true,
			_ => (areaModeStats.Completed || saveData.CheatMode || saveData.DebugMode) && modeProperties.TotalStrawberries > 0,
		};

		return toReturn;
	}

	private static void ModCassette(ILContext il) {
		ILCursor cursor = new(il);
		while (cursor.TryGotoNext(MoveType.After, instr => instr.MatchLdfld<AreaStats>("Cassette"))) {
			cursor.EmitDelegate<Func<bool>>(IsEnabledCassette);
			cursor.EmitNot();
			cursor.EmitAnd();
		}
	}

	private static bool IsEnabledCassette() {
		return Settings.CassetteToggle;
	}

	private static void ModCrystalHeart(ILContext il) {
		ILCursor cursor = new(il);
		while (cursor.TryGotoNext(MoveType.After, instr => instr.MatchLdfld<AreaModeStats>("HeartGem"))) {
			cursor.EmitDelegate<Func<bool>>(IsEnabledCrystalHeart);
			cursor.EmitNot();
			cursor.EmitAnd();
		}
	}

	private static bool IsEnabledCrystalHeart() {
		return Settings.HeartGemToggle;
	}

	private static void ModSummitGem(ILContext il) {
		ILCursor cursor = new(il);
		while (cursor.TryGotoNext(MoveType.After, instr => instr.MatchLdfld<SaveData>("SummitGems"))) {
			// The logic for determining the Summit gem coloration is
			// annoying to modify directly, so just add an additional
			// conditional jump depending on the setting
			ILLabel label = null;
			cursor.GotoNext(MoveType.After, instr => instr.MatchBrfalse(out label));
			cursor.EmitDelegate<Func<bool>>(IsEnabledSummitGem);
			cursor.EmitBrtrue(label);
		}
	}

	private static bool IsEnabledSummitGem() {
		return Settings.SummitGemToggle;
	}
}
