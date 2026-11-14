using System;
using System.Reflection;
using GeneralWaybilling.UI;
using GeneralWaybilling.Zones;
using HarmonyLib;
using Model.Ops;
using Railroader.Modding;

namespace GeneralWaybilling
{
    /// <summary>
    /// Mod entry point and lifecycle manager for General Waybilling.
    /// </summary>
    public sealed class ModEntryPoint : IModEntryPoint, IWorldLifecycle
    {
        private static IModLogger? logger;
        public static IModLogger Log => logger ?? NullModLogger.Instance;

        private sealed class NullModLogger : IModLogger
        {
            public static readonly NullModLogger Instance = new NullModLogger();
            public void Debug(string message) { }
            public void Info(string message) { }
            public void Warning(string message) { }
            public void Error(string message) { }
            public void Error(Exception exception, string message) { }
        }

        private Harmony? harmony;

        public void OnModLoad(IModContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            logger = context.Log;
            Log.Info("GeneralWaybilling initializing via Railroader.Modding contract.");

            try
            {
                harmony = new Harmony("CRZBP.GeneralWaybilling");
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                Log.Info("Harmony patches applied successfully.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to apply Harmony patches.");
            }

            try
            {
                CarInspectorOverrideSection.Register();
                Log.Info("CarInspectorOverrideSection registered.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to register CarInspectorOverrideSection.");
            }

            // If world is already loaded, register destinations immediately
            if (OpsController.Shared != null)
            {
                OnWorldLoad();
            }
        }

        public void OnModUnload()
        {
            OnWorldUnload();

            try
            {
                harmony?.UnpatchAll("CRZBP.GeneralWaybilling");
                harmony = null;
                Log.Info("GeneralWaybilling harmony unpatched.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to unpatch Harmony.");
            }

            Log.Info("GeneralWaybilling unloaded.");
        }

        public void OnWorldLoad()
        {
            try
            {
                WaybillZoneRegistry.Initialize();
                CarInspectorOverrideSection.Register();

                OpsController? shared = OpsController.Shared;
                if (shared != null)
                {
                    GeneralWaybillRegistry.RegisterForOpsController(shared);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed during OnWorldLoad registration.");
            }
        }

        public void OnWorldUnload()
        {
            try
            {
                GeneralWaybillRegistry.UnregisterAll();
                WaybillZoneRegistry.Cleanup();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed during OnWorldUnload cleanup.");
            }
        }
    }
}
