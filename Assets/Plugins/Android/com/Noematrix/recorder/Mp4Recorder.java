package com.Noematrix.recorder;

import android.util.Log;

/**
 * Unity 调用入口（唯一）
 */
public class Mp4Recorder {

    private static final String TAG = "Mp4Recorder";

    private static EncoderThread encoderThread;
    private static boolean isRecording = false;

    public static synchronized void start(
            String outputPath,
            int width,
            int height,
            int fps
    ) {
        if (isRecording) {
            Log.w(TAG, "start() called but already recording");
            return;
        }

        Log.i(TAG, "Start recording: " + outputPath);

        encoderThread =
                new EncoderThread(outputPath, width, height, fps);
        encoderThread.start();

        isRecording = true;
    }

    /**
     * Unity -> Java
     * C# sbyte[] 会自动映射成 Java byte[]
     */
    public static synchronized void pushFrame(
            byte[] yuv420,
            long ptsUs
    ) {
        if (!isRecording || encoderThread == null) {
            return;
        }

        encoderThread.enqueueFrame(yuv420, ptsUs);
    }

    public static synchronized void stop() {
        if (!isRecording) {
            Log.w(TAG, "stop() called but not recording");
            return;
        }

        Log.i(TAG, "Stopping recorder");

        if (encoderThread != null) {
            encoderThread.requestStop();
            try {
                encoderThread.join();
            } catch (InterruptedException ignored) {}
            encoderThread = null;
        }

        isRecording = false;
    }
}
