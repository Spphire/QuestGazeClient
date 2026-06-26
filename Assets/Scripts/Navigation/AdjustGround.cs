using System;
using UnityEngine;
using Anaglyph.XRTemplate;
using TMPro;

[DisallowMultipleComponent]
public class AdjustGround : MonoBehaviour
{
    [Header("Ground Nodes")]
    [Tooltip("用于向下 Raycast 的参考点（通常是 center）")]
    public Transform rayOrigin;

    [Tooltip("模型最低点（用于计算站立高度）")]
    public Transform bottom;

    [Header("Ground Settings")]
    public float checkDistance = 10f;
    public float groundOffset = 0.0f;

    [Header("Adjust Settings")]
    [Tooltip("Y 轴收敛速度")]
    public float adjustSpeed = 8f;

    [Tooltip("小于该距离直接吸附，防抖")]
    public float snapThreshold = 0.05f;

    [Tooltip("最大允许的高度差，超过则视为探测有误")]
    public float maxSnapDistance = 0.8f;

    // =========================
    // 输出给外部使用的 Y 速度
    // =========================
    public float VerticalVelocity { get; private set; }

    private EnvironmentMapper.RayResult hit;

    public GameObject groundPoint;
    void Reset()
    {
        rayOrigin = transform;
        bottom = transform;
    }

    private void Start()
    {
        //groundPoint = FindAnyObjectByType<GroundShow>().gameObject;
    }

    void Update()
    {
        if (rayOrigin == null || bottom == null)
        {
            VerticalVelocity = 0f;
            return;
        }

        UpdateVerticalVelocity();
    }

    // =========================
    // 核心逻辑：只算 Y
    // =========================
    void UpdateVerticalVelocity()
    {
        bool hasGround = EnvironmentMapper.Raycast(
            new Ray(rayOrigin.position, Vector3.down),
            checkDistance,
            out hit,
            EnvironmentMapper.RaycastMode.Negative
        );

        if (!hasGround)
        {
            // 没地面 → 自由下落
            VerticalVelocity += Physics.gravity.y * Time.deltaTime;
            return;
        }

        
        float targetY = hit.point.y + groundOffset - bottom.localPosition.y;
        float currentY = transform.position.y;
        float deltaY = targetY - currentY;
        
        if (groundPoint != null)
        {
            groundPoint.transform.position = new Vector3(
                hit.point.x, 
                targetY, 
                hit.point.z
                );
        }
        
        // ---------- 防止异常吸附 ----------
        if (Mathf.Abs(deltaY) > maxSnapDistance)
        {
            VerticalVelocity = 0f;
            return;
        }
        
        // ---------- 小范围直接贴地 ----------
        if (Mathf.Abs(deltaY) < snapThreshold)
        {
            VerticalVelocity = deltaY * adjustSpeed;
        }
        else
        {
            // ---------- 平滑收敛 ----------
            float desiredVel = deltaY * adjustSpeed;
            VerticalVelocity = Mathf.Lerp(
                VerticalVelocity,
                desiredVel,
                adjustSpeed * Time.deltaTime
            );
        }
    }

    // =========================
    // 外部主动调用（Teleport / Spawn）
    // =========================
    public void SnapToGroundImmediate()
    {
        if (rayOrigin == null || bottom == null)
            return;

        if (EnvironmentMapper.Raycast(
            new Ray(rayOrigin.position, Vector3.down),
            checkDistance,
            out hit,
            EnvironmentMapper.RaycastMode.Negative
        ))
        {
            Vector3 pos = transform.position;
            pos.y = hit.point.y + groundOffset - bottom.localPosition.y;
            transform.position = pos;
            VerticalVelocity = 0f;
        }
    }
}
