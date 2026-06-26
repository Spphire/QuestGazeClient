using System;
using UnityEngine;
using System.Collections.Generic;
using System.Diagnostics;
using TMPro;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using Debug = UnityEngine.Debug;

public class LocalGridLazyThetaNavigator : LocalGridNavigator
{
    // =========================
    // PUBLIC API
    // =========================
    public override List<Vector3> FindPath(Vector3 targetWorld)
    {
        BuildLocalGridJobified(targetWorld);
        return RunLazyTheta(targetWorld);
    }
    
    // =========================
    // LAZY THETA*
    // =========================
    List<Vector3> RunLazyTheta(Vector3 targetWorld)
    {
        Vector2Int gStart = globalGrid.WorldToGrid(transform.position);
        Vector2Int gGoal = globalGrid.WorldToGrid(targetWorld);

        Node start = new Node(gStart.x - gxMin, gStart.y - gzMin);
        Node goal  = new Node(gGoal.x  - gxMin, gGoal.y  - gzMin);

        var open = new PriorityQueue<Node>();
        var cameFrom = new Dictionary<Node, Node>();
        var cost = new Dictionary<Node, float>();

        cameFrom[start] = start;
        cost[start] = 0;
        open.Enqueue(start, Heuristic(start, goal));
        
        int expandLimit = 15000;   // 你可以从 5000~20000 试
        int expanded = 0;

        while (open.Count > 0)
        {
            if (++expanded > expandLimit)
                return null; // 放弃本次路径，下一次再算
            
            Node cur = open.Dequeue();

            // Lazy correction
            Node parent = cameFrom[cur];
            if (!parent.Equals(cur) && !LineOfSight(parent, cur))
            {
                float bestCost = float.PositiveInfinity;
                Node bestParent = cur;

                foreach (Node n in Neighbors(cur))
                {
                    if (localGrid[n.x, n.z] == 1) continue;
                    if (!cost.ContainsKey(n)) continue;

                    float c = cost[n] + Distance(n, cur);
                    if (c < bestCost)
                    {
                        bestCost = c;
                        bestParent = n;
                    }
                }

                cameFrom[cur] = bestParent;
                cost[cur] = bestCost;
            }

            if (cur.Equals(goal))
                return ReconstructPath(cameFrom, cur);

            foreach (Node n in Neighbors(cur))
            {
                if (localGrid[n.x, n.z] == 1)
                    continue;

                float newCost = cost[cur] + Distance(cur, n);

                if (localGrid[n.x, n.z] == -1)
                    newCost += allowUnknown ? unknownPenalty : 999f;

                if (!cost.ContainsKey(n) || newCost < cost[n])
                {
                    cost[n] = newCost;
                    cameFrom[n] = cur;
                    open.Enqueue(n, newCost + Heuristic(n, goal));
                }
            }
        }
        return null;
    }

    // =========================
    // PATH
    // =========================
    List<Vector3> ReconstructPath(Dictionary<Node, Node> cameFrom, Node end)
    {
        List<Vector3> path = new();
        Node cur = end;
        
        int guard = 0;
        while (!cameFrom[cur].Equals(cur) && guard++ < 10000)
        {
            Vector2Int g = new Vector2Int(gxMin + cur.x, gzMin + cur.z);
            if (g.y * 1024 + g.x >= 0 && g.y * 1024 + g.x < 1024 * 1024 && visualize)
            {
                localPixels[g.y * 1024 + g.x] = new Color32(255, 0, 255, 255);
            }
            path.Add(globalGrid.GridToWorld(g));
            cur = cameFrom[cur];
        }
        if(visualize)
            textureDirty = true;
        path.Reverse();
        return path;
    }

    // =========================
    // GEOMETRY
    // =========================
    float Distance(Node a, Node b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    float Heuristic(Node a, Node b) => Distance(a, b);

    bool LineOfSight(Node a, Node b)
    {
        int x0 = a.x, z0 = a.z;
        int x1 = b.x, z1 = b.z;

        int dx = Mathf.Abs(x1 - x0);
        int dz = Mathf.Abs(z1 - z0);
        int sx = x0 < x1 ? 1 : -1;
        int sz = z0 < z1 ? 1 : -1;
        int err = dx - dz;

        int maxSteps = Mathf.Max(dx, dz) + 2;
        int steps = 0;

        while (steps++ < maxSteps)
        {
            if (localGrid[x0, z0] == 1)
                return false;

            if (x0 == x1 && z0 == z1)
                return true;

            int e2 = err * 2;
            if (e2 > -dz) { err -= dz; x0 += sx; }
            if (e2 < dx)  { err += dx; z0 += sz; }
        }

        // fail-safe
        return false;
    }

    IEnumerable<Node> Neighbors(Node n)
    {
        for (int dx = -1; dx <= 1; dx++)
        for (int dz = -1; dz <= 1; dz++)
        {
            if (dx == 0 && dz == 0) continue;

            int nx = n.x + dx;
            int nz = n.z + dz;

            if (nx >= 0 && nz >= 0 && nx < width && nz < height)
                yield return new Node(nx, nz);
        }
    }
}

