using System.Collections.Generic;
using UnityEngine;
using Anaglyph.XRTemplate;
using TMPro;
using UnityEngine.InputSystem;

public class GlobalGridMapper : MonoBehaviour
{
    [Header("Grid Settings")]
    public int gridSize = 1024;
    public float cellSize = 0.1f;

    // 0 unknown, 1 free, 2 occupied
    public byte[,] grid;

    [Header("Player Sensor")]
    public Transform playerHead;
    public float maxRayLength = 10f;
    public float[] verticalOffsets = { 0.2f, 0.5f, 0.8f };
    public int raysPerLayer = 18;
    
    [Header("XR Input")]
    public InputActionReference adjustAlphaAction;
    public float alphaAdjustSpeed = 0.5f;
    
    [Header("Visualization")]
    public bool visualize = false;
    public Renderer localGridRenderer;
    public Renderer gridRenderer;
    [Range(0,1f)]
    public float visualizeAlpha;

    Texture2D gridTexture;
    Color32[] pixels;
    bool textureDirty;
    
    [Header("RaycastBatch")]
    private readonly List<Ray> rayBuffer = new();
    private readonly List<(int angleIndex, int yIndex)> rayMap = new();

    void Awake()
    {
        grid = new byte[gridSize, gridSize];

        gridTexture = new Texture2D(
            gridSize,
            gridSize,
            TextureFormat.RGBA32,
            false,
            true
        );
        gridTexture.filterMode = FilterMode.Point;
        gridTexture.wrapMode = TextureWrapMode.Clamp;

        pixels = new Color32[gridSize * gridSize];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = new Color32(0, 0, 255, 255);

        gridTexture.SetPixels32(pixels);
        gridTexture.Apply();

        gridRenderer.materials[0].SetTexture("_BaseMap", gridTexture);
            //.mainTexture = gridTexture;

        if (!playerHead)
            playerHead = Camera.main.transform;
    }
    
    void OnEnable()
    {
        adjustAlphaAction?.action?.Enable();
    }

    void OnDisable()
    {
        adjustAlphaAction?.action?.Disable();
    }

    void Update()
    {
        UpdateGridFromPlayer();
        UpdateAlphaFromXR();
    }
    
    void UpdateAlphaFromXR()
    {
        if (adjustAlphaAction == null)
            return;

        Vector2 axis = adjustAlphaAction.action.ReadValue<Vector2>();
        
        //FindFirstObjectByType<TextMeshPro>().text = axis.y.ToString();

        float delta = axis.y * alphaAdjustSpeed * Time.deltaTime;
        if (Mathf.Abs(delta) < 0.0001f)
            return;

        visualizeAlpha = Mathf.Clamp01(visualizeAlpha + delta);
        gridRenderer.materials[0].SetFloat("_Alpha", visualizeAlpha);
        localGridRenderer.materials[0].SetFloat("_Alpha", visualizeAlpha);
    }

    void LateUpdate()
    {
        if (visualize)
        {
            if (!textureDirty) return;

            gridTexture.SetPixels32(pixels);
            gridTexture.Apply(false);

            textureDirty = false;
        }
    }

    #region Grid Update

    /*void UpdateGridFromPlayer()
    {
        if (!Physics.Raycast(playerHead.position, Vector3.down, out RaycastHit floorHit, maxRayLength))
            return;

        float floorY = floorHit.point.y;
        Vector3 headPos = playerHead.position;

        float angleOffset = Random.Range(0, 360f / raysPerLayer);

        for (int i = 0; i < raysPerLayer; i++)
        {
            float angle = i * (360f / raysPerLayer) + angleOffset;
            Vector3 dir = Quaternion.Euler(0, angle, 0) * Vector3.forward;

            Vector3 nearestHit = Vector3.zero;
            bool hit = false;

            foreach (float y in verticalOffsets)
            {
                Vector3 origin = new Vector3(headPos.x, floorY + y, headPos.z);
                Ray ray = new Ray(origin, dir);

                if (EnvironmentMapper.Raycast(ray, maxRayLength, out EnvironmentMapper.RayResult r, EnvironmentMapper.RaycastMode.Negative))
                {
                    if (!hit)
                    {
                        nearestHit = r.point;
                        hit = true;
                    }else if ((r.point - origin).sqrMagnitude < (nearestHit - origin).sqrMagnitude)
                    {
                        nearestHit = r.point;
                    }
                }
            }

            if (hit)
            {
                UpdateGridAlongRay(headPos, nearestHit);
                MarkOccupied(nearestHit);
            }
            else
            {
                UpdateGridAlongRay(headPos, headPos + dir * maxRayLength);
            }
        }
    }
    */

    void UpdateGridFromPlayer()
    {
        bool hasGround = EnvironmentMapper.Raycast(
            new Ray(playerHead.position, Vector3.down),
            5f,
            out EnvironmentMapper.RayResult floorHit,
            EnvironmentMapper.RaycastMode.Negative
        );
        
        if(!hasGround)return;

        float floorY = floorHit.point.y;
        Vector3 headPos = playerHead.position;

        float angleOffset = Random.Range(0, 360f / raysPerLayer);

        rayBuffer.Clear();
        rayMap.Clear();

        // ---- build rays ----
        for (int i = 0; i < raysPerLayer; i++)
        {
            float angle = i * (360f / raysPerLayer) + angleOffset;
            Vector3 dir = Quaternion.Euler(0, angle, 0) * Vector3.forward;

            for (int y = 0; y < verticalOffsets.Length; y++)
            {
                Vector3 origin = new Vector3(headPos.x, floorY + verticalOffsets[y], headPos.z);
                rayBuffer.Add(new Ray(origin, dir));
                rayMap.Add((i, y));
            }
        }

        // ---- batch raycast ----
        EnvironmentMapper.RayResult[] results = EnvironmentMapper.RaycastBatch(
            rayBuffer.ToArray(),
            maxRayLength,
            EnvironmentMapper.RaycastMode.Negative
        );
        //FindFirstObjectByType<TextMeshPro>().text = results.Length.ToString();

        // ---- reduce per angle ----
        Vector3[] nearestHits = new Vector3[raysPerLayer];
        bool[] hitFlags = new bool[raysPerLayer];

        for (int i = 0; i < results.Length; i++)
        {
            if (!results[i].didHit)
                continue;

            int angleIndex = rayMap[i].angleIndex;
            Vector3 origin = rayBuffer[i].origin;

            if (!hitFlags[angleIndex])
            {
                nearestHits[angleIndex] = results[i].point;
                hitFlags[angleIndex] = true;
            }
            else
            {
                float d0 = (nearestHits[angleIndex] - origin).sqrMagnitude;
                float d1 = (results[i].point - origin).sqrMagnitude;
                if (d1 < d0)
                    nearestHits[angleIndex] = results[i].point;
            }
        }

        // ---- grid update (完全复用你原逻辑) ----
        for (int i = 0; i < raysPerLayer; i++)
        {
            float angle = i * (360f / raysPerLayer) + angleOffset;
            Vector3 dir = Quaternion.Euler(0, angle, 0) * Vector3.forward;

            if (hitFlags[i])
            {
                UpdateGridAlongRay(headPos, nearestHits[i]);
                MarkOccupied(nearestHits[i]);
            }
            else
            {
                UpdateGridAlongRay(headPos, headPos + dir * maxRayLength);
            }
        }
    }

    void UpdateGridAlongRay(Vector3 start, Vector3 end)
    {
        float dist = Vector3.Distance(start, end);
        int steps = Mathf.CeilToInt(dist / cellSize);
        Vector3 step = (end - start) / steps;
        Vector3 pos = start;

        for (int i = 0; i < steps; i++)
        {
            Vector2Int idx = WorldToGrid(pos);
            if (InBounds(idx))
                SetCell(idx, 1);

            pos += step;
        }
    }

    void MarkOccupied(Vector3 worldPos)
    {
        Vector2Int idx = WorldToGrid(worldPos);
        if (InBounds(idx))
            SetCell(idx, 2);
    }

    void SetCell(Vector2Int idx, byte state)
    {
        if (grid[idx.x, idx.y] == state)
            return;

        grid[idx.x, idx.y] = state;
        if (visualize)
        {
            int p = idx.y * gridSize + idx.x;
            if (state == 0)
            {
                pixels[p] = new Color32(0, 0, 255, 255);
            }
            else if (state == 1)
            {
                pixels[p] = new Color32(0, 255, 0, 255);
            }
            else if (state == 2)
            {
                pixels[p] = new Color32(255, 0, 0, 255);
            }
            textureDirty = true;
        }
    }

    #endregion

    #region Grid Helpers

    public Vector2Int WorldToGrid(Vector3 worldPos)
    {
        int gx = Mathf.FloorToInt((worldPos.x - EnvironmentMapper.Instance.WorldBounds.min.x) / cellSize);
        int gz = Mathf.FloorToInt((worldPos.z - EnvironmentMapper.Instance.WorldBounds.min.z) / cellSize);
        return new Vector2Int(gx, gz);
    }

    public Vector3 GridToWorld(Vector2Int idx)
    {
        float x = idx.x * cellSize + EnvironmentMapper.Instance.WorldBounds.min.x + cellSize * 0.5f;
        float z = idx.y * cellSize + EnvironmentMapper.Instance.WorldBounds.min.z + cellSize * 0.5f;
        return new Vector3(x, 0f, z);
    }

    public bool InBounds(Vector2Int idx)
    {
        return idx.x >= 0 && idx.y >= 0 && idx.x < gridSize && idx.y < gridSize;
    }

    #endregion
}
