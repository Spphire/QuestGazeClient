using System.Collections.Generic;
using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Debug = UnityEngine.Debug;

public abstract class LocalGridNavigator : MonoBehaviour
{
    // =========================
    // REFERENCES
    // =========================
    [Header("References")]
    public GlobalGridMapper globalGrid;
    public CharacterRaySensor character;

    // =========================
    // LOCAL WINDOW
    // =========================
    [Header("Local Window")]
    public float extraMarginWorld = 5.0f;

    // =========================
    // A*
    // =========================
    [Header("A*")]
    public bool allowUnknown = true;
    public float unknownPenalty = 5f;

    // =========================
    // INTERNAL GRID
    // =========================
    // -1 unknown, 0 free, 1 occupied
    protected int[,] localGrid;
    NativeArray<byte> croppedGlobal;
    NativeArray<sbyte> localGridNA;

    protected int gxMin, gxMax, gzMin, gzMax;
    protected int width, height;
    private int clearanceRadiusCells;

    private readonly Stopwatch gridStopwatch = new Stopwatch();
    
    [Header("Visualization")]
    public bool visualize = true;
    public Renderer localGridRenderer;
    Texture2D localGridTexture;
    protected bool textureDirty=false;
    protected Color32[] localPixels;
    List<Vector3> prevPath;
    
    private void Awake()
    {
        localGridTexture = new Texture2D(
            1024, 
            1024,
            TextureFormat.RGBA32,
            false,
            true
        );
        localGridTexture.filterMode = FilterMode.Point;
        localGridTexture.wrapMode = TextureWrapMode.Clamp;
        
        localPixels =  new Color32[1024*1024];
        for (int i = 0; i < localPixels.Length; i++)
            localPixels[i] = new Color32(0, 0, 0, 0);
        
        localGridTexture.SetPixels32(localPixels);
        localGridTexture.Apply();
        
        localGrid = new int[1024, 1024];
        croppedGlobal = new NativeArray<byte>(1024*1024, Allocator.Persistent);
        localGridNA = new NativeArray<sbyte>(1024*1024, Allocator.Persistent);
    }
    
    private void Start()
    {
        globalGrid = FindFirstObjectByType<GlobalGridMapper>();
        localGridRenderer = globalGrid.transform.Find("LocalGrid").GetComponent<Renderer>();
        localGridRenderer.materials[0].SetTexture("_BaseMap", localGridTexture);
        if (character == null)
        {
            character = GetComponent<CharacterRaySensor>();
        }
    }
    
    void LateUpdate()
    {
        if (visualize)
        {
            if (!textureDirty) return;
            localGridTexture.SetPixels32(localPixels);
            localGridTexture.Apply(false);
            textureDirty = false;
        }
    }
    
    private void OnDestroy()
    {
        if (croppedGlobal.IsCreated) croppedGlobal.Dispose();
        if (localGridNA.IsCreated) localGridNA.Dispose();
    }

    
    // =========================
    // PUBLIC API
    // =========================
    public virtual List<Vector3> FindPath(Vector3 targetWorld)
    {
        BuildLocalGridJobified(targetWorld);
        return new List<Vector3>();
    }
    
    public void FillColor(Color32 color, int minX, int maxX, int minZ, int maxZ)
    {
        for (int x = minX; x <= maxX; x++)
        {
            for (int z = minZ; z <= maxZ; z++)
            {
                if (z * 1024 + x >= 0 &&  z * 1024 + x < 1024 * 1024)
                    localPixels[z * 1024 + x] = color;
            }
        }
    }
    
    // =========================
    // LOCAL GRID BUILD (JOB)
    // =========================
    protected void BuildLocalGridJobified(Vector3 targetWorld)
    {
        gridStopwatch.Restart();
        
        if(visualize)
            FillColor(new Color32(0, 0, 0, 0), gxMin, gxMax, gzMin, gzMax);
        // =========================
        // 1. Window & Params
        // =========================
        Vector2Int gAI = globalGrid.WorldToGrid(transform.position);
        Vector2Int gTarget = globalGrid.WorldToGrid(targetWorld);

        clearanceRadiusCells =
            Mathf.CeilToInt(character.radius / globalGrid.cellSize);

        int marginCells =
            Mathf.CeilToInt(extraMarginWorld / globalGrid.cellSize);

        gxMin = Mathf.Min(gAI.x, gTarget.x) - clearanceRadiusCells - marginCells;
        gxMax = Mathf.Max(gAI.x, gTarget.x) + clearanceRadiusCells + marginCells;
        gzMin = Mathf.Min(gAI.y, gTarget.y) - clearanceRadiusCells - marginCells;
        gzMax = Mathf.Max(gAI.y, gTarget.y) + clearanceRadiusCells + marginCells;
        
        if(visualize)
            FillColor(new Color32(0, 0, 0, 255), gxMin, gxMax, gzMin, gzMax);

        width = gxMax - gxMin + 1;
        height = gzMax - gzMin + 1;

        int cellCount = width * height;

        long tWindow = gridStopwatch.ElapsedTicks;

        // =========================
        // 2. Allocate Native Arrays
        // =========================

        long tAlloc = gridStopwatch.ElapsedTicks;

        // =========================
        // 3. Copy global → cropped
        // =========================
        for (int lx = 0; lx < width; lx++)
        for (int lz = 0; lz < height; lz++)
        {
            int gx = gxMin + lx;
            int gz = gzMin + lz;
            int idx = lx + lz * width;

            if (!globalGrid.InBounds(new Vector2Int(gx, gz)))
            {
                croppedGlobal[idx] = 2;
            }
            else
            {
                croppedGlobal[idx] = globalGrid.grid[gx, gz];
            }
        }

        long tCopy = gridStopwatch.ElapsedTicks;

        // =========================
        // 4. Schedule + Run Job
        // =========================
        var job = new ClearanceJob
        {
            croppedGlobal = croppedGlobal,
            localGrid = localGridNA,
            width = width,
            height = height,
            clearanceRadius = clearanceRadiusCells
        };

        JobHandle handle = job.Schedule(cellCount, 64);
        handle.Complete();

        long tJob = gridStopwatch.ElapsedTicks;

        // =========================
        // 5. Copy back to managed
        // =========================
        for (int lx = 0; lx < width; lx++)
        for (int lz = 0; lz < height; lz++)
        {
            localGrid[lx, lz] = localGridNA[lx + lz * width];
            if (visualize)
            {
                int gx = gxMin + lx;
                int gz = gzMin + lz;
                if (gz * 1024 + gx >= 0 && gz * 1024 + gx < 1024 * 1024)
                {
                    if (localGrid[lx, lz] == -1)
                    {
                        localPixels[gz * 1024 + gx] = new Color32(0, 0, 255, 255);
                    }else if (localGrid[lx, lz] == 0)
                    {
                        localPixels[gz * 1024 + gx] = new Color32(0, 255, 0, 255);
                    }
                    else if (localGrid[lx, lz] == 1)
                    {
                        localPixels[gz * 1024 + gx] = new Color32(255, 0, 0, 255);
                    }
                }
            }
        }

        long tBack = gridStopwatch.ElapsedTicks;

        gridStopwatch.Stop();

        // =========================
        // 6. Log (ms)
        // =========================
        double tickMs = 1000.0 / Stopwatch.Frequency;

        string log = $"[LocalGrid Build]\n" +
                     $" Window: {(tWindow) * tickMs:F3} ms\n" +
                     $" Alloc: {(tAlloc - tWindow) * tickMs:F3} ms\n" +
                     $" Copy: {(tCopy - tAlloc) * tickMs:F3} ms\n" +
                     $" Job: {(tJob - tCopy) * tickMs:F3} ms\n" +
                     $" Back: {(tBack - tJob) * tickMs:F3} ms\n" +
                     $" TOTAL: {gridStopwatch.ElapsedMilliseconds} ms\n" +
                     $" Size: {width}x{height} ({cellCount})";
        Debug.Log(log);

        //FindFirstObjectByType<TextMeshPro>().text = log;
    }
    
    
    
    // =========================
    // DATA STRUCT
    // =========================
    protected struct Node
    {
        public int x, z;
        public Node(int x, int z)
        {
            this.x = x;
            this.z = z;
        }

        public override int GetHashCode()
        {
            return x * 73856093 ^ z * 19349663;
        }

        public override bool Equals(object obj)
        {
            if (!(obj is Node)) return false;
            Node n = (Node)obj;
            return n.x == x && n.z == z;
        }
    }
}

// =========================
// CLEARANCE JOB
// =========================
[BurstCompile]
public struct ClearanceJob : IJobParallelFor
{
    [ReadOnly, NativeDisableParallelForRestriction] public NativeArray<byte> croppedGlobal;
    [WriteOnly]public NativeArray<sbyte> localGrid;

    public int width;
    public int height;
    public int clearanceRadius;

    public void Execute(int index)
    {
        int lx = index % width;
        int lz = index / width;

        byte g = croppedGlobal[index];

        if (g == 0)
        {
            localGrid[index] = -1;
            return;
        }

        if (g == 2)
        {
            localGrid[index] = 1;
            return;
        }

        for (int dx = -clearanceRadius; dx <= clearanceRadius; dx++)
        for (int dz = -clearanceRadius; dz <= clearanceRadius; dz++)
        {
            if(dx*dx+dz*dz>clearanceRadius*clearanceRadius)continue;
            
            int nx = lx + dx;
            int nz = lz + dz;

            if (nx < 0 || nz < 0 || nx >= width || nz >= height)
            {
                localGrid[index] = 1;
                return;
            }

            if (croppedGlobal[nx + nz * width] != 1)
            {
                localGrid[index] = 1;
                return;
            }
        }

        localGrid[index] = 0;
    }
}



// =========================
// PRIORITY QUEUE
// =========================
public class PriorityQueue<T>
{
    private readonly List<(T item, float priority)> elements = new();

    public int Count => elements.Count;

    public void Enqueue(T item, float priority)
    {
        elements.Add((item, priority));
    }

    public T Dequeue()
    {
        int bestIndex = 0;
        float bestPriority = elements[0].priority;

        for (int i = 1; i < elements.Count; i++)
        {
            if (elements[i].priority < bestPriority)
            {
                bestPriority = elements[i].priority;
                bestIndex = i;
            }
        }

        T bestItem = elements[bestIndex].item;
        elements.RemoveAt(bestIndex);
        return bestItem;
    }
}