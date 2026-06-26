package com.Noematrix.encoder;

import android.media.MediaCodec;
import android.media.MediaCodecInfo;
import android.media.MediaFormat;
import android.media.MediaMuxer;
import android.opengl.EGL14;
import android.opengl.EGLConfig;
import android.opengl.EGLContext;
import android.opengl.EGLDisplay;
import android.opengl.EGLSurface;
import android.opengl.GLES20;
import android.view.Surface;
import android.util.Log;

import java.io.IOException;
import java.nio.ByteBuffer;

public class AndroidEncoder {

    private static final String TAG = "AndroidEncoder";

    private MediaCodec encoder;
    private MediaMuxer muxer;
    private Surface inputSurface;

    private EGLDisplay eglDisplay;
    private EGLContext eglContext;
    private EGLSurface eglSurface;

    private int width;
    private int height;
    private int fps;

    private int trackIndex = -1;
    private boolean muxerStarted = false;

    // ================================
    // 构造函数（Unity C# 调用）
    // ================================
    public AndroidEncoder(
            String path,
            int width,
            int height,
            int fps,
            int bitrate) throws IOException {

        this.width = width;
        this.height = height;
        this.fps = fps;

        prepareEncoder(path, bitrate);
        prepareEGL();
    }

    // ================================
    // MediaCodec 初始化
    // ================================
    private void prepareEncoder(String path, int bitrate) throws IOException {

        MediaFormat format = MediaFormat.createVideoFormat(
                MediaFormat.MIMETYPE_VIDEO_AVC,
                width, height);

        format.setInteger(MediaFormat.KEY_COLOR_FORMAT,
                MediaCodecInfo.CodecCapabilities.COLOR_FormatSurface);
        format.setInteger(MediaFormat.KEY_BIT_RATE, bitrate);
        format.setInteger(MediaFormat.KEY_FRAME_RATE, fps);
        format.setInteger(MediaFormat.KEY_I_FRAME_INTERVAL, 1);

        encoder = MediaCodec.createEncoderByType(
                MediaFormat.MIMETYPE_VIDEO_AVC);
        encoder.configure(format, null, null,
                MediaCodec.CONFIGURE_FLAG_ENCODE);

        inputSurface = encoder.createInputSurface();
        encoder.start();

        muxer = new MediaMuxer(
                path,
                MediaMuxer.OutputFormat.MUXER_OUTPUT_MPEG_4);

        Log.i(TAG, "Encoder prepared");
    }

    // ================================
    // EGL 初始化（核心）
    // ================================
    private void prepareEGL() {

        eglDisplay = EGL14.eglGetDisplay(EGL14.EGL_DEFAULT_DISPLAY);
        if (eglDisplay == EGL14.EGL_NO_DISPLAY) {
            throw new RuntimeException("eglGetDisplay failed");
        }

        int[] version = new int[2];
        EGL14.eglInitialize(eglDisplay, version, 0, version, 1);

        EGLConfig config = chooseEglConfig();

        // ⚠️ 关键：使用当前 Unity 的 EGLContext
        EGLContext currentContext = EGL14.eglGetCurrentContext();
        if (currentContext == EGL14.EGL_NO_CONTEXT) {
            throw new RuntimeException("No current EGLContext (call from GL thread!)");
        }

        int[] contextAttribs = {
                EGL14.EGL_CONTEXT_CLIENT_VERSION, 2,
                EGL14.EGL_NONE
        };

        eglContext = EGL14.eglCreateContext(
                eglDisplay, config,
                currentContext,
                contextAttribs, 0);

        int[] surfaceAttribs = {
                EGL14.EGL_NONE
        };

        eglSurface = EGL14.eglCreateWindowSurface(
                eglDisplay, config,
                inputSurface,
                surfaceAttribs, 0);

        Log.i(TAG, "EGL prepared");
    }

    private EGLConfig chooseEglConfig() {
        int[] attribs = {
                EGL14.EGL_RED_SIZE, 8,
                EGL14.EGL_GREEN_SIZE, 8,
                EGL14.EGL_BLUE_SIZE, 8,
                EGL14.EGL_ALPHA_SIZE, 8,
                EGL14.EGL_RENDERABLE_TYPE, EGL14.EGL_OPENGL_ES2_BIT,
                EGL14.EGL_NONE
        };

        EGLConfig[] configs = new EGLConfig[1];
        int[] numConfigs = new int[1];
        EGL14.eglChooseConfig(
                eglDisplay, attribs, 0,
                configs, 0, 1,
                numConfigs, 0);

        return configs[0];
    }

    // ================================
    // Unity 每帧调用（通过 IssuePluginEvent）
    // ================================
    public void renderFrame() {

        EGL14.eglMakeCurrent(
                eglDisplay,
                eglSurface,
                eglSurface,
                eglContext);

        GLES20.glViewport(0, 0, width, height);

        // ⚠️ Unity 已经画好，我们只需要 swap
        EGL14.eglSwapBuffers(eglDisplay, eglSurface);

        drainEncoder(false);
    }

    // ================================
    // 读取编码输出
    // ================================
    private void drainEncoder(boolean endOfStream) {

        if (endOfStream) {
            encoder.signalEndOfInputStream();
        }

        MediaCodec.BufferInfo bufferInfo = new MediaCodec.BufferInfo();

        while (true) {
            int outputIndex = encoder.dequeueOutputBuffer(bufferInfo, 0);

            if (outputIndex == MediaCodec.INFO_TRY_AGAIN_LATER) {
                break;
            } else if (outputIndex == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED) {

                if (muxerStarted) {
                    throw new RuntimeException("Format changed twice");
                }

                MediaFormat newFormat = encoder.getOutputFormat();
                trackIndex = muxer.addTrack(newFormat);
                muxer.start();
                muxerStarted = true;

            } else if (outputIndex >= 0) {

                if (!muxerStarted) {
                    throw new RuntimeException("Muxer not started");
                }

                ByteBuffer encodedData =
                        encoder.getOutputBuffer(outputIndex);

                muxer.writeSampleData(
                        trackIndex,
                        encodedData,
                        bufferInfo);

                encoder.releaseOutputBuffer(outputIndex, false);
            }
        }
    }

    // ================================
    // 停止录制
    // ================================
    public void stop() {

        drainEncoder(true);

        encoder.stop();
        encoder.release();

        muxer.stop();
        muxer.release();

        EGL14.eglDestroySurface(eglDisplay, eglSurface);
        EGL14.eglDestroyContext(eglDisplay, eglContext);
        EGL14.eglTerminate(eglDisplay);

        Log.i(TAG, "Encoder stopped");
    }
}