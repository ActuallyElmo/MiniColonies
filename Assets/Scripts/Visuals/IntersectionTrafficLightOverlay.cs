using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Always-visible world indicators for player-authored traffic lights.</summary>
[DisallowMultipleComponent]
public sealed class IntersectionTrafficLightOverlay : MonoBehaviour
{
    private sealed class SignalMarker
    {
        public Vector2Int Cell;
        public LaneId Lane;
        public Transform Transform;
        public SpriteRenderer Renderer;
        public Vector3 FullScale;
        public Color PendingColor;
        public float AnimationTime;
        public bool Changing;
        public bool ColorApplied;
    }

    private TrafficSystemBackend traffic;
    private IntersectionBuilderStyle appearance = IntersectionBuilderStyle.Default;
    private readonly List<SignalMarker> markers = new List<SignalMarker>();
    private GameObject visualRoot;

    public void Initialize(TrafficSystemBackend backend, IntersectionBuilderStyle style)
    {
        Unsubscribe();
        traffic = backend;
        appearance = style.Validated();
        Subscribe();
        Rebuild();
    }

    public void ApplyAppearance(IntersectionBuilderStyle style)
    {
        style = style.Validated();
        if (appearance.Equals(style)) return;
        appearance = style;
        Rebuild();
    }

    private void OnEnable()
    {
        Subscribe();
        if (traffic != null) Rebuild();
    }

    private void Subscribe()
    {
        Unsubscribe();
        if (traffic == null) return;
        traffic.OnTrafficNetworkReady += Rebuild;
        traffic.OnTrafficLightStateChanged += HandleLightChanged;
    }

    private void Unsubscribe()
    {
        if (traffic == null) return;
        traffic.OnTrafficNetworkReady -= Rebuild;
        traffic.OnTrafficLightStateChanged -= HandleLightChanged;
    }

    private void Update()
    {
        float duration = Mathf.Max(0.01f, appearance.trafficLightPopDuration);
        for (int i = 0; i < markers.Count; i++)
        {
            SignalMarker marker = markers[i];
            if (marker.Transform == null || marker.AnimationTime < 0f) continue;
            marker.AnimationTime += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(marker.AnimationTime / duration);
            if (!marker.Changing)
            {
                marker.Transform.localScale = marker.FullScale * PopIn01(
                    progress,
                    appearance.trafficLightPopOvershoot);
            }
            else if (progress < 0.5f)
            {
                marker.Transform.localScale = marker.FullScale *
                    (1f - Smooth01(progress * 2f));
            }
            else
            {
                if (!marker.ColorApplied && marker.Renderer != null)
                {
                    marker.Renderer.color = marker.PendingColor;
                    marker.ColorApplied = true;
                }
                marker.Transform.localScale = marker.FullScale *
                    PopIn01(
                        (progress - 0.5f) * 2f,
                        appearance.trafficLightPopOvershoot);
            }
            if (progress >= 1f)
            {
                marker.Transform.localScale = marker.FullScale;
                marker.AnimationTime = -1f;
            }
        }
    }

    private void Rebuild()
    {
        TrafficGraphSnapshot graph = traffic != null
            ? traffic.CurrentTrafficGraphSnapshot
            : null;
        if (!isActiveAndEnabled || graph == null)
        {
            Clear();
            return;
        }
        if (visualRoot == null)
            visualRoot = new GameObject("Intersection traffic-light indicators");

        var previousMarkers = new List<SignalMarker>(markers);
        markers.Clear();

        for (int nodeIndex = 0; nodeIndex < graph.ControlledNodes.Count; nodeIndex++)
        {
            ControlledNodeRecord node = graph.ControlledNodes[nodeIndex];
            if (node.NodeKind != RoadNodeKind.Intersection ||
                !IntersectionControlUtility.UsesTrafficLights(node.RuleType)) continue;
            for (int laneIndex = 0; laneIndex < node.IncomingLaneIds.Count; laneIndex++)
            {
                if (!graph.TryGetLane(node.IncomingLaneIds[laneIndex], out var lane) ||
                    !graph.TryGetGeometry(lane.GeometryId, out var geometry) ||
                    geometry.Samples.Count == 0) continue;
                Vector3 position = geometry.Samples[geometry.Samples.Count - 1];
                Vector2 outward = IntersectionControlUtility.DirectionVector(lane.EndLegDirectionBit);
                position += new Vector3(outward.x, 0f, outward.y) *
                    appearance.trafficLightIconOutwardOffset;
                position += Vector3.up * appearance.trafficLightIconHeightOffset;
                bool green = traffic.TryGetTrafficLightState(
                    node.GridPosition, lane.Id, out bool state) && state;
                SignalMarker marker = TakeMarker(
                    previousMarkers,
                    node.GridPosition,
                    lane.Id);
                if (marker == null)
                    marker = CreateMarker(
                        node.GridPosition,
                        lane.Id,
                        position,
                        green);
                else
                    RefreshMarker(marker, position, green);
                markers.Add(marker);
            }
        }

        for (int i = 0; i < previousMarkers.Count; i++)
            DestroyMarker(previousMarkers[i]);
    }

    private SignalMarker CreateMarker(
        Vector2Int cell,
        LaneId lane,
        Vector3 position,
        bool green)
    {
        var markerObject = new GameObject("Traffic light indicator", typeof(SpriteRenderer));
        markerObject.transform.SetParent(visualRoot.transform, false);
        SpriteRenderer renderer = markerObject.GetComponent<SpriteRenderer>();
        renderer.sortingOrder = 6;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        var marker = new SignalMarker
        {
            Cell = cell,
            Lane = lane,
            Transform = markerObject.transform,
            Renderer = renderer,
            AnimationTime = 0f,
            Changing = false,
            ColorApplied = true
        };
        ConfigureMarker(marker, position);
        renderer.color = SignalColor(green);
        markerObject.transform.localScale = Vector3.zero;
        return marker;
    }

    private void RefreshMarker(SignalMarker marker, Vector3 position, bool green)
    {
        ConfigureMarker(marker, position);
        Color target = SignalColor(green);
        bool alreadyChanging = marker.Changing &&
            marker.AnimationTime >= 0f &&
            marker.PendingColor == target;
        if (!alreadyChanging && marker.Renderer.color != target)
            StartColorChange(marker, target);
        else if (marker.AnimationTime < 0f)
            marker.Transform.localScale = marker.FullScale;
    }

    private void ConfigureMarker(SignalMarker marker, Vector3 position)
    {
        marker.Transform.SetParent(visualRoot.transform, false);
        marker.Transform.position = position;
        marker.Transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        marker.Renderer.sprite = appearance.GetTrafficLightSignalSprite();
        float sourceSize = marker.Renderer.sprite != null
            ? Mathf.Max(
                marker.Renderer.sprite.bounds.size.x,
                marker.Renderer.sprite.bounds.size.y)
            : 0f;
        marker.FullScale = sourceSize > 0.0001f
            ? Vector3.one * (appearance.trafficLightIconRadius * 2f / sourceSize)
            : Vector3.one;
    }

    private static SignalMarker TakeMarker(
        List<SignalMarker> candidates,
        Vector2Int cell,
        LaneId lane)
    {
        for (int i = 0; i < candidates.Count; i++)
        {
            SignalMarker marker = candidates[i];
            if (marker.Cell != cell || marker.Lane != lane) continue;
            candidates.RemoveAt(i);
            return marker;
        }
        return null;
    }

    private void HandleLightChanged(Vector2Int cell, int activeSignalIndex)
    {
        for (int i = 0; i < markers.Count; i++)
        {
            SignalMarker marker = markers[i];
            if (marker.Cell != cell || marker.Renderer == null) continue;
            bool green = traffic.TryGetTrafficLightState(cell, marker.Lane, out bool state) && state;
            Color target = green
                ? appearance.trafficLightGreenColor
                : appearance.trafficLightRedColor;
            if (marker.Renderer.color == target && marker.AnimationTime < 0f) continue;
            StartColorChange(marker, target);
        }
    }

    private Color SignalColor(bool green) => green
        ? appearance.trafficLightGreenColor
        : appearance.trafficLightRedColor;

    private static void StartColorChange(SignalMarker marker, Color target)
    {
        marker.PendingColor = target;
        marker.AnimationTime = 0f;
        marker.Changing = true;
        marker.ColorApplied = false;
    }

    public static float Smooth01(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * (3f - 2f * value);
    }

    public static float PopIn01(float value, float overshoot)
    {
        value = Mathf.Clamp01(value) - 1f;
        float back = 1.70158f * Mathf.Clamp(overshoot, 0f, 2f);
        return 1f + (back + 1f) * value * value * value +
            back * value * value;
    }

    private static void DestroyMarker(SignalMarker marker)
    {
        if (marker == null || marker.Transform == null) return;
        marker.Transform.gameObject.SetActive(false);
        if (Application.isPlaying) Destroy(marker.Transform.gameObject);
        else DestroyImmediate(marker.Transform.gameObject);
    }

    private void Clear()
    {
        markers.Clear();
        if (visualRoot == null) return;
        visualRoot.SetActive(false);
        if (Application.isPlaying) Destroy(visualRoot);
        else DestroyImmediate(visualRoot);
        visualRoot = null;
    }

    private void OnDisable()
    {
        Unsubscribe();
        Clear();
    }
}
