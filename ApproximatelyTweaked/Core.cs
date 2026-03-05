using HarmonyLib;
using MelonLoader;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[assembly: MelonInfo(typeof(ApproximatelyTweaked.Core), "ApproximatelyTweaked", "1.0.0", "Ben McAvoy", null)]
[assembly: MelonGame("ApproximatelyGames", "ApproximatelyUp")]

namespace ApproximatelyTweaked
{
    public class Core : MelonMod
    {
        private static readonly AccessTools.FieldRef<UIHandComponentsList, UIHandComponentItem[]> uiHandComponentsListItemsFieldRef =
            AccessTools.FieldRefAccess<UIHandComponentsList, UIHandComponentItem[]>("_items");

        private static global::Core.Singleton? coreSingleton;
        private static GarageGrabberSingleton? garageSingleton;

        private int[] remainingCounts = Array.Empty<int>();

        private readonly int DONT_SHOW_NUM_VAL = 1000000;

        public override void OnUpdate()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;

            var entityManager = world.EntityManager;

            coreSingleton ??= Utility.GetSingleton<global::Core.Singleton>(entityManager);
            garageSingleton ??= Utility.GetSingleton<GarageGrabberSingleton>(entityManager);

            if (coreSingleton is not { } core || garageSingleton is not { } garage)
                return;

            var handComponentList = UIManager._singleton?._handComponentsList;
            if (handComponentList == null) return;

            var items = uiHandComponentsListItemsFieldRef(handComponentList);

            if (remainingCounts.Length != items.Length)
                remainingCounts = new int[items.Length];

            for (int i = 0; i < items.Length; i++)
            {
                var item = items[i];

                if (item == null || item._prefab == null)
                {
                    remainingCounts[i] = DONT_SHOW_NUM_VAL;
                    continue;
                }

                var componentData = entityManager.GetComponentData<SCPrefab>(EntityPrefabComponent.Get(item._prefab));
                int availableComponents = core.GetAvailableComponents(componentData);

                if (availableComponents < 1)
                    continue;

                remainingCounts[i] = availableComponents - GetUsedComponents(componentData, garage);
            }
        }

        private static GUIStyle? _labelStyle;
        private static GUIStyle LabelStyle
        {
            get
            {
                if (_labelStyle != null) return _labelStyle;
                _labelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 16,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                };
                _labelStyle.normal.textColor = Color.white;
                return _labelStyle;
            }
        }

        private static GUIStyle? _shadowStyle;
        private static GUIStyle ShadowStyle
        {
            get
            {
                if (_shadowStyle != null) return _shadowStyle;
                _shadowStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 16,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                };
                _shadowStyle.normal.textColor = Color.black;
                return _shadowStyle;
            }
        }

        private static GUIStyle? _compassLabelStyle;
        private static GUIStyle CompassLabelStyle
        {
            get
            {
                if (_compassLabelStyle != null) return _compassLabelStyle;
                _compassLabelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 13,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.UpperCenter,
                };
                _compassLabelStyle.normal.textColor = Color.white;
                return _compassLabelStyle;
            }
        }

        private static GUIStyle? _compassShadowStyle;
        private static GUIStyle CompassShadowStyle
        {
            get
            {
                if (_compassShadowStyle != null) return _compassShadowStyle;
                _compassShadowStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 13,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.UpperCenter,
                };
                _compassShadowStyle.normal.textColor = Color.black;
                return _compassShadowStyle;
            }
        }

        // Compass constants
        private const float COMPASS_WIDTH = 480f;
        private const float COMPASS_HEIGHT = 36f;
        private const float COMPASS_SCALE = 2.4f; // pixels per degree — 480px = 200° visible

        // ECS query for the active rendering camera.
        private EntityQuery _cameraQuery;
        private bool _cameraQueryCreated = false;

        private float GetCameraHeading()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return 0f;

            var em = world.EntityManager;

            if (!_cameraQueryCreated)
            {
                _cameraQuery = em.CreateEntityQuery(ComponentType.ReadOnly<CRPCameraData>());
                _cameraQueryCreated = true;
            }

            // Find the first rendering CRPCameraData entity.
            using var entities = _cameraQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
            int bestPriority = int.MinValue;
            float4x4 bestViewMatrix = float4x4.identity;
            bool found = false;
            foreach (var entity in entities)
            {
                var camData = em.GetComponentData<CRPCameraData>(entity);
                if (!camData._rendering) continue;
                if (!found || camData._priority > bestPriority)
                {
                    bestPriority = camData._priority;
                    bestViewMatrix = camData._viewMatrix;
                    found = true;
                }
            }

            if (!found) return 0f;

            // The view matrix is world-to-camera. Invert it to get camera-to-world,
            // then extract the forward vector and compute yaw.
            var camToWorld = math.inverse(bestViewMatrix);
            // Forward in camera space is -Z; transform to world space.
            float3 forward = new float3(-camToWorld.c2.x, -camToWorld.c2.y, -camToWorld.c2.z);
            // Flatten onto XZ plane to remove pitch influence, then compute yaw.
            float3 flatForward = math.normalize(new float3(forward.x, 0f, forward.z));
            float yaw = math.degrees(math.atan2(flatForward.x, flatForward.z));
            if (yaw < 0f) yaw += 360f;
            return yaw;
        }

        private void DrawCompass()
        {
            float cx = Screen.width / 2f;
            float barTop = 8f;

            float heading = GetCameraHeading();

            // --- Background bar ---
            var bgRect = new Rect(cx - COMPASS_WIDTH / 2f, barTop, COMPASS_WIDTH, COMPASS_HEIGHT);
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(bgRect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.BeginGroup(bgRect);

            float centerX = COMPASS_WIDTH / 2f;

            // --- Tick marks every 10° in world space ---
            for (int worldDeg = 0; worldDeg < 360; worldDeg += 10)
            {
                float diff = Mathf.DeltaAngle(heading, worldDeg);
                float xPos = centerX + diff * COMPASS_SCALE;

                if (xPos < 0f || xPos > COMPASS_WIDTH) continue;

                bool isMajor = worldDeg % 45 == 0;
                float tickH = isMajor ? 14f : 7f;
                GUI.color = isMajor ? Color.white : new Color(1f, 1f, 1f, 0.55f);
                GUI.DrawTexture(new Rect(xPos - 0.5f, COMPASS_HEIGHT - tickH, 1f, tickH), Texture2D.whiteTexture);
            }

            // --- Cardinal / inter-cardinal labels ---
            (float deg, string label, bool isCardinal)[] cardinals =
            {
                (  0f, "N",  true),  ( 45f, "NE", false), ( 90f, "E",  true), (135f, "SE", false),
                (180f, "S",  true),  (225f, "SW", false), (270f, "W",  true), (315f, "NW", false),
                (360f, "N",  true),  // wrap-around copy so N appears on both edges
            };

            foreach (var (deg, label, isCardinal) in cardinals)
            {
                float diff = Mathf.DeltaAngle(heading, deg);
                float xPos = centerX + diff * COMPASS_SCALE;

                if (xPos < -25f || xPos > COMPASS_WIDTH + 25f) continue;

                var r = new Rect(xPos - 15f, 2f, 30f, 20f);
                GUI.color = Color.black; // shadow
                GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), label, CompassShadowStyle);
                GUI.color = isCardinal ? Color.yellow : Color.white;
                GUI.Label(r, label, CompassLabelStyle);
            }

            // --- Center marker line ---
            GUI.color = new Color(1f, 0.9f, 0f, 0.9f);
            GUI.DrawTexture(new Rect(centerX - 0.5f, 0f, 1f, COMPASS_HEIGHT), Texture2D.whiteTexture);

            GUI.color = Color.white;
            GUI.EndGroup();

            // --- Heading readout below bar ---
            int deg360 = Mathf.RoundToInt(heading) % 360;
            string headingText = $"{deg360:000}°";
            var readoutRect = new Rect(cx - 25f, barTop + COMPASS_HEIGHT + 1f, 50f, 18f);
            GUI.Label(new Rect(readoutRect.x + 1f, readoutRect.y + 1f, readoutRect.width, readoutRect.height), headingText, ShadowStyle);
            GUI.Label(readoutRect, headingText, LabelStyle);
        }

        public override void OnGUI()
        {
            DrawCompass();

            var handComponentList = UIManager._singleton?._handComponentsList;
            if (handComponentList == null) return;

            var items = uiHandComponentsListItemsFieldRef(handComponentList);

            for (int i = 0; i < items.Length && i < remainingCounts.Length; i++)
            {
                var item = items[i];
                if (item == null) continue;

                var count = remainingCounts[i];
                if (count == DONT_SHOW_NUM_VAL)
                    continue;

                var screenPos = item.transform.position;
                var guiPos = new Vector2(screenPos.x, Screen.height - screenPos.y);
                var rect = new Rect(guiPos.x - 25f, guiPos.y + 10f, 50f, 20f);
                var label = count.ToString();

                // 1-pixel drop shadow for legibility against any background.
                GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), label, ShadowStyle);
                GUI.Label(rect, label, LabelStyle);
            }
        }

        private static unsafe int GetUsedComponents(SCPrefab prefab, GarageGrabberSingleton garage)
        {
            var map = *garage._usedComponentsMapPtr;
            return map[prefab];
        }
    }
}