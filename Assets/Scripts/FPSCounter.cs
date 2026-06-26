using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;

public class FPSCounter : MonoBehaviour
{
    [Header("Settings")]
    public float sampleDuration = 5f;   // 统计窗口（秒）
    public int maxSamples = 3000;        // 最大帧样本数

    private List<float> frameTimes = new List<float>();

    private float timer;
    private float currentFPS;
    private float avgFPS;
    private float minFPS;
    private float maxFPS;
    private float onePercentLow;
    private float pointOnePercentLow;
    
    TextMeshPro textMesh;
    private void Start()
    {
        textMesh = FindFirstObjectByType<TextMeshPro>();
    }

    void Update()
    {
        float frameTime = Time.unscaledDeltaTime;
        float fps = 1f / frameTime;

        currentFPS = fps;

        frameTimes.Add(frameTime);
        if (frameTimes.Count > maxSamples)
            frameTimes.RemoveAt(0);

        timer += frameTime;
        if (timer >= sampleDuration)
        {
            timer = 0f;
            CalculateStats();
            textMesh.text = GetStatsString();
        }
        
    }

    void CalculateStats()
    {
        if (frameTimes.Count == 0)
            return;

        // FPS 列表
        List<float> fpsList = frameTimes
            .Select(t => 1f / t)
            .OrderBy(f => f)
            .ToList();

        avgFPS = fpsList.Average();
        minFPS = fpsList.First();
        maxFPS = fpsList.Last();

        onePercentLow = GetPercentile(fpsList, 1f);
        pointOnePercentLow = GetPercentile(fpsList, 0.1f);
    }

    float GetPercentile(List<float> sortedList, float percentile)
    {
        if (sortedList.Count == 0)
            return 0f;

        float index = (percentile / 100f) * (sortedList.Count - 1);
        int lower = Mathf.FloorToInt(index);
        int upper = Mathf.CeilToInt(index);

        if (lower == upper)
            return sortedList[lower];

        float t = index - lower;
        return Mathf.Lerp(sortedList[lower], sortedList[upper], t);
    }

    public string GetStatsString()
    {
        float frameTimeMs = currentFPS > 0 ? 1000f / currentFPS : 0f;

        StringBuilder sb = new StringBuilder(256);

        sb.AppendLine($"FPS: {currentFPS:F1}");
        sb.AppendLine($"Frame Time: {frameTimeMs:F2} ms");
        sb.AppendLine($"Avg FPS: {avgFPS:F1}");
        sb.AppendLine($"1% Low: {onePercentLow:F1}");
        sb.AppendLine($"0.1% Low: {pointOnePercentLow:F1}");
        sb.AppendLine($"Min FPS: {minFPS:F1}");
        sb.AppendLine($"Max FPS: {maxFPS:F1}");

        return sb.ToString();
    }
}