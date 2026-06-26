using UnityEngine;
using System.Collections.Generic;

public class LocalGridAStarNavigator : LocalGridNavigator
{
    // =========================
    // PUBLIC API
    // =========================
    public override List<Vector3> FindPath(Vector3 targetWorld)
    {
        BuildLocalGridJobified(targetWorld);
        return RunAStar(targetWorld);
    }

    // =========================
    // A*
    // =========================
    List<Vector3> RunAStar(Vector3 targetWorld)
    {
        Vector2Int gStart = globalGrid.WorldToGrid(transform.position);
        Vector2Int gGoal = globalGrid.WorldToGrid(targetWorld);

        Node start = new Node(gStart.x - gxMin, gStart.y - gzMin);
        Node goal = new Node(gGoal.x - gxMin, gGoal.y - gzMin);

        var open = new PriorityQueue<Node>();
        var cameFrom = new Dictionary<Node, Node>();
        var cost = new Dictionary<Node, float>();

        open.Enqueue(start, 0);
        cost[start] = 0;

        while (open.Count > 0)
        {
            Node cur = open.Dequeue();
            if (cur.Equals(goal))
                return ReconstructPath(cameFrom, cur);

            foreach (Node n in Neighbors(cur))
            {
                if (localGrid[n.x, n.z] == 1)
                    continue;

                float newCost = cost[cur] + 1f;
                if (localGrid[n.x, n.z] == -1)
                    newCost += allowUnknown ? unknownPenalty : 999f;

                if (!cost.ContainsKey(n) || newCost < cost[n])
                {
                    cost[n] = newCost;
                    open.Enqueue(n, newCost + Heuristic(n, goal));
                    cameFrom[n] = cur;
                }
            }
        }
        return null;
    }
    
    protected List<Vector3> ReconstructPath(Dictionary<Node, Node> cameFrom, Node end)
    {
        List<Vector3> path = new();
        Node cur = end;

        while (cameFrom.ContainsKey(cur))
        {
            Vector2Int g =
                new Vector2Int(gxMin + cur.x, gzMin + cur.z);
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

    protected float Heuristic(Node a, Node b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.z - b.z);
    }

    protected IEnumerable<Node> Neighbors(Node n)
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


