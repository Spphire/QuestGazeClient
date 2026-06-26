package com.Noematrix.recorder;

import android.util.Log;

import java.util.concurrent.ConcurrentHashMap;

/**
 * 多路 MP4 录制管理器
 *
 * Unity 通过 recorderId 区分不同录制实例
 */
public class Mp4RecorderList {

    private static final String TAG = "Mp4RecorderList";

    /**
     * recorderId -> EncoderThread
     */
    private static final ConcurrentHashMap<Integer, EncoderThread> recorderMap =
            new ConcurrentHashMap<>();

    /**
     * 启动一个录制实例
     */
    public static synchronized void start(
            int recorderId,
            String outputPath,
            int width,
            int height,
            int fps
    ) {
        if (recorderMap.containsKey(recorderId)) {
            Log.w(TAG, "Recorder " + recorderId + " already exists");
            return;
        }

        Log.i(TAG, "Start recorder " + recorderId + ": " + outputPath);

        EncoderThread encoder =
                new EncoderThread(outputPath, width, height, fps);
        encoder.start();

        recorderMap.put(recorderId, encoder);
    }

    /**
     * 推送一帧 YUV420 数据
     * Unity -> Java
     * C# sbyte[] / byte[] 会自动映射成 Java byte[]
     */
    public static void pushFrame(
            int recorderId,
            byte[] yuv420,
            long ptsUs
    ) {
        EncoderThread encoder = recorderMap.get(recorderId);
        if (encoder == null) {
            return;
        }

        encoder.enqueueFrame(yuv420, ptsUs);
    }

    /**
     * 停止指定 recorder
     */
    public static synchronized String stop(int recorderId) {
        EncoderThread encoder = recorderMap.remove(recorderId);
        if (encoder == null) {
            Log.w(TAG, "Recorder " + recorderId + " not found");
            return null;
        }

        Log.i(TAG, "Stopping recorder " + recorderId);

        try {
            encoder.requestStop();
            encoder.join(); // 在后台线程等待 EncoderThread 完成
            Log.i(TAG, "Recorder " + recorderId + " finished saving");
            Log.e(TAG, encoder.getSha256());
            return encoder.getSha256();
        } catch (InterruptedException ignored) {
            return null;
        }
    }

    /**
     * 停止所有 recorder
     */
    public static synchronized void stopAll() {
        for (Integer recorderId : recorderMap.keySet()) {
            stop(recorderId);
        }
    }

    /**
     * 当前录制实例数量（调试用）
     */
    public static int getRecorderCount() {
        return recorderMap.size();
    }
}
