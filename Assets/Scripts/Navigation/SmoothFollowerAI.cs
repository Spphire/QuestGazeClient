using System;
using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(CharacterController))]
public class SmoothFollowerAI : MonoBehaviour
{
    [Header("Target")]
    public Transform player;

    [Header("Follow Distance")]
    public float followDistance = 1.0f;
    public float repathDistance = 1.5f;

    [Header("Movement")]
    public float moveSpeed = 1.0f;
    public float acceleration = 3.0f;
    public float rotationSpeed = 8.0f;

    [Header("Navigation")]
    public LocalGridNavigator navigator;
    public float pathUpdateInterval = 0.3f;

    [Header("Path Following")]
    public float waypointReachDist = 0.2f;
    public float lookAheadDistance = 0.8f;

    // -------------------------

    private CharacterController controller;
    private AdjustGround adjustGround;

    private Vector3 velocity;
    private float pathTimer;

    private List<Vector3> currentPath;
    private int pathIndex = 0;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
    }

    void Start()
    {
        player = Camera.main.transform;
        adjustGround = GetComponent<AdjustGround>();
    }

    void Update()
    {
        if (player == null || navigator == null)
            return;

        float distToPlayer = FlatDistance(transform.position, player.position);

        // =====================
        // 重新寻路
        // =====================
        pathTimer += Time.deltaTime;

        if (distToPlayer > repathDistance && pathTimer >= pathUpdateInterval)
        {
            pathTimer = 0f;
            currentPath = navigator.FindPath(player.position);
            pathIndex = 0;
        }

        // =====================
        // 路径节点推进
        // =====================
        AdvancePathIndex();

        // =====================
        // Look Ahead 跟随
        // =====================
        if (currentPath != null && pathIndex < currentPath.Count)
        {
            Vector3 target = GetLookAheadTarget();
            MoveTowards(target);
        }
        else
        {
            SlowDown();
        }
    }

    // -------------------------
    // Path Logic
    // -------------------------

    void AdvancePathIndex()
    {
        if (currentPath == null)
            return;

        while (pathIndex < currentPath.Count &&
               FlatDistance(transform.position, currentPath[pathIndex]) < waypointReachDist)
        {
            pathIndex++;
        }
    }

    Vector3 GetLookAheadTarget()
    {
        Vector3 pos = transform.position;

        for (int i = pathIndex; i < currentPath.Count - 1; i++)
        {
            Vector3 a = currentPath[i];
            Vector3 b = currentPath[i + 1];

            a.y = pos.y;
            b.y = pos.y;

            Vector3 ab = b - a;
            Vector3 ap = pos - a;

            float t = Vector3.Dot(ap, ab) / ab.sqrMagnitude;
            t = Mathf.Clamp01(t);

            Vector3 closest = a + ab * t;
            float dist = Vector3.Distance(pos, closest);

            if (dist < lookAheadDistance)
            {
                float remaining = lookAheadDistance - dist;
                return closest + ab.normalized * remaining;
            }
        }

        return currentPath[currentPath.Count - 1];
    }

    // -------------------------
    // Movement
    // -------------------------

    void MoveTowards(Vector3 target)
    {
        Vector3 toTarget = target - transform.position;
        toTarget.y = 0f;

        if (toTarget.sqrMagnitude < 0.001f)
            return;

        Vector3 desiredVelocity = toTarget.normalized * moveSpeed;

        velocity = Vector3.Lerp(
            velocity,
            desiredVelocity,
            acceleration * Time.deltaTime
        );

        controller.Move(
            new Vector3(velocity.x, adjustGround.VerticalVelocity, velocity.z) * Time.deltaTime
        );

        if (velocity.sqrMagnitude > 0.01f)
        {
            Quaternion targetRot = Quaternion.LookRotation(velocity.normalized);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRot,
                rotationSpeed * Time.deltaTime
            );
        }
    }

    void SlowDown()
    {
        velocity = Vector3.Lerp(
            velocity,
            Vector3.zero,
            acceleration * Time.deltaTime
        );
    }

    // -------------------------
    // Utils
    // -------------------------

    float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    public void Teleport(Vector3 position)
    {
        currentPath = null;
        velocity = Vector3.zero;
        pathIndex = 0;
        transform.position = position;
    }
}
