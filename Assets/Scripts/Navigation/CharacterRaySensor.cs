using UnityEngine;
using System.Collections.Generic;
using Anaglyph.XRTemplate;

public class CharacterRaySensor : MonoBehaviour
{
    [Header("Cylinder Parameters")]
    public float radius = 0.4f;

    [Header("Vertical Ray Layers (Local Y offsets)")]
    public List<float> verticalOffsets = new List<float> { 0.2f, 1.2f , 2.0f};

    [Header("Ray Layout")]
    public int raysPerLayer = 36;
    public float maxRayLength = 2.5f;

    [Header("Update")]
    public float updateInterval = 0.15f;
    private float timer;

    [Header("Visualization (Runtime / Quest)")]
    public bool visualize = true;
    public float lineWidth = 0.01f;
    public Material rayMaterial;
    public Color freeColor = Color.green;
    public Color hitColor = Color.red;

    // ------------------------

    struct RayInfo
    {
        public Vector3 origin;
        public Vector3 end;
        public bool hit;
    }

    private readonly List<RayInfo> rays = new();
    private readonly List<LineRenderer> linePool = new();

    void Start()
    {
        AllocateLineRenderers();
    }

    void Update()
    {
        timer += Time.deltaTime;
        if (timer >= updateInterval)
        {
            timer = 0f;
            UpdateRays();
            UpdateVisualization();
        }
    }

    // ------------------------
    // Ray logic
    // ------------------------
    void UpdateRays()
    {
        rays.Clear();

        float angleStep = 360f / raysPerLayer;
        float angleOffset = Random.Range(0, angleStep);

        foreach (float yOffset in verticalOffsets)
        {
            for (int i = 0; i < raysPerLayer; i++)
            {
                float angle = i * angleStep + angleOffset;
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * transform.forward;

                Vector3 origin =
                    transform.position +
                    transform.up * yOffset +
                    dir * radius;

                float length = maxRayLength;
                bool hitSomething = false;

                Ray ray = new Ray(origin, dir);

                if (EnvironmentMapper.Raycast(ray, maxRayLength, out EnvironmentMapper.RayResult hit, EnvironmentMapper.RaycastMode.Negative))
                {
                    length = hit.distance;
                    hitSomething = true;
                }

                rays.Add(new RayInfo
                {
                    origin = origin,
                    end = origin + dir * length,
                    hit = hitSomething
                });
            }
        }
    }

    // ------------------------
    // LineRenderer visualization
    // ------------------------
    void AllocateLineRenderers()
    {
        int required = raysPerLayer * verticalOffsets.Count;

        for (int i = 0; i < required; i++)
        {
            GameObject go = new GameObject($"RayLine_{i}");
            go.transform.SetParent(transform, false);

            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.startWidth = lineWidth;
            lr.endWidth = lineWidth;
            lr.useWorldSpace = true;
            lr.material = rayMaterial;
            lr.enabled = visualize;

            linePool.Add(lr);
        }
    }

    void UpdateVisualization()
    {
        if (!visualize)
        {
            foreach (var lr in linePool)
                lr.enabled = false;
            return;
        }

        int count = Mathf.Min(rays.Count, linePool.Count);

        for (int i = 0; i < count; i++)
        {
            LineRenderer lr = linePool[i];
            RayInfo r = rays[i];

            lr.enabled = true;
            lr.SetPosition(0, r.origin);
            lr.SetPosition(1, r.end);
            lr.startColor = lr.endColor = r.hit ? hitColor : freeColor;
        }

        // 多余的线隐藏
        for (int i = count; i < linePool.Count; i++)
            linePool[i].enabled = false;
    }
    
    public IReadOnlyList<(Vector3 origin, Vector3 end, bool hit)> GetRays()
    {
        List<(Vector3, Vector3, bool)> list = new();
        foreach (var r in rays)
            list.Add((r.origin, r.end, r.hit));
        return list;
    }

}

