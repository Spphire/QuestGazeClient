package com.Noematrix.recorder;

import android.media.MediaCodec;
import android.media.MediaCodecInfo;
import android.media.MediaFormat;
import android.media.MediaMuxer;
import android.util.Log;

import java.io.IOException;
import java.nio.ByteBuffer;
import java.util.concurrent.ArrayBlockingQueue;
import java.util.concurrent.TimeUnit;

import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;

class EncoderThread extends Thread {

    private static final String TAG = "EncoderThread";
    private static final String MIME_TYPE = "video/avc";

    private final String outputPath;
    private final int width;
    private final int height;
    private final int fps;
    private final int yuvSize;

    private MediaCodec codec;
    private MediaMuxer muxer;

    private int trackIndex = -1;
    private boolean muxerStarted = false;

    private volatile boolean stopRequested = false;
    private boolean eosSent = false;
    private boolean receivedFirstFrame = false;
    
    // ===== sha256结果 =====
    private MessageDigest sha256;
    private volatile String finalSha256;
    public String getSha256() {
        return finalSha256;
    }

    // ===== 编码统计 =====
    private long totalEnqueuedFrames = 0;

    private long encodedFrameCount = 0;
    private long lastLogTimeMs = 0;

    private static class Frame {
        byte[] data;
        long ptsUs;

        Frame(byte[] data, long ptsUs) {
            this.data = data;
            this.ptsUs = ptsUs;
        }
    }

    private final ArrayBlockingQueue<Frame> frameQueue =
            new ArrayBlockingQueue<>(10);

    private final ArrayBlockingQueue<byte[]> bufferPool =
            new ArrayBlockingQueue<>(10);

    EncoderThread(String path, int w, int h, int fps) {
        this.outputPath = path;
        this.width = w;
        this.height = h;
        this.fps = fps;
        this.yuvSize = w * h * 3 / 2;

        for (int i = 0; i < 10; i++) {
            bufferPool.offer(new byte[yuvSize]);
        }
    }

    @Override
    public void run() {
        try {
            initEncoder();
            encodeLoop();
        } catch (Exception e) {
            Log.e(TAG, "Encoder error", e);
        } finally {
            release();
        }
    }

    // ================= Unity 调用 =================

    void enqueueFrame(byte[] yuv420, long ptsUs) {
        if (stopRequested) return;

        totalEnqueuedFrames++;

        long now = System.currentTimeMillis();
        if (now - lastLogTimeMs > 1000) {
            lastLogTimeMs = now;
            Log.i(TAG,
                "enqueueFrame count=" + totalEnqueuedFrames +
                " queue=" + frameQueue.size() +
                " ptsUs=" + ptsUs +
                " yuvLen=" + (yuv420 != null ? yuv420.length : -1)
            );
        }

        if (yuv420 == null || yuv420.length < yuvSize) {
            Log.e(TAG, "Invalid YUV buffer!");
            return;
        }

        byte[] buffer = bufferPool.poll();
        if (buffer == null) {
            Log.w(TAG, "Frame dropped (no buffer)");
            return;
        }

        System.arraycopy(yuv420, 0, buffer, 0, yuvSize);

        if (!frameQueue.offer(new Frame(buffer, ptsUs))) {
            bufferPool.offer(buffer);
            Log.w(TAG, "Frame dropped (queue full)");
        }
    }

    void requestStop() {
        stopRequested = true;
        interrupt();
    }

    // ================= Encoder =================

    private void initEncoder() throws IOException {
        try {
            sha256 = MessageDigest.getInstance("SHA-256");
        } catch (NoSuchAlgorithmException e) {
            throw new RuntimeException(e);
        }
        
        int bitrate = width * height * fps / 2;

        MediaFormat format =
                MediaFormat.createVideoFormat(MIME_TYPE, width, height);

        format.setInteger(
                MediaFormat.KEY_COLOR_FORMAT,
                MediaCodecInfo.CodecCapabilities.COLOR_FormatYUV420SemiPlanar);

        format.setInteger(MediaFormat.KEY_BIT_RATE, bitrate);
        format.setInteger(MediaFormat.KEY_FRAME_RATE, fps);
        format.setInteger(MediaFormat.KEY_I_FRAME_INTERVAL, 1);

        codec = MediaCodec.createEncoderByType(MIME_TYPE);
        codec.configure(format, null, null,
                MediaCodec.CONFIGURE_FLAG_ENCODE);
        codec.start();

        muxer = new MediaMuxer(
                outputPath,
                MediaMuxer.OutputFormat.MUXER_OUTPUT_MPEG_4);

        Log.i(TAG, "Encoder initialized (I420), bitrate=" + bitrate);
    }

    private void encodeLoop() {
        MediaCodec.BufferInfo info =
                new MediaCodec.BufferInfo();

        while (true) {

            Frame frame = null;
            try {
                frame = frameQueue.poll(20, TimeUnit.MILLISECONDS);
            } catch (InterruptedException ignored) {}

            if (frame != null) {
                receivedFirstFrame = true;
                feedInput(frame);
                bufferPool.offer(frame.data);
            }

            drainOutput(info);

            if (stopRequested && frameQueue.isEmpty()) {
                break;
            }
        }

        // ===== EOS =====
        if (!eosSent && receivedFirstFrame) {
            sendEOS();
            drainUntilEOS(info);
        }
    }

    private void feedInput(Frame frame) {
        int index = codec.dequeueInputBuffer(10_000);
        if (index < 0) return;

        ByteBuffer buf = codec.getInputBuffer(index);
        if (buf == null) return;

        buf.clear();
        buf.put(frame.data, 0, yuvSize);

        codec.queueInputBuffer(
                index, 0, yuvSize, frame.ptsUs, 0);
    }

    private void sendEOS() {
        if (eosSent) return;

        while (true) {
            int index = codec.dequeueInputBuffer(10_000);
            if (index >= 0) {
                codec.queueInputBuffer(
                        index,
                        0,
                        0,
                        0,
                        MediaCodec.BUFFER_FLAG_END_OF_STREAM);
                eosSent = true;
                Log.i(TAG, "EOS queued");
                break;
            }
        }
    }

    // ================= drain =================

    private void drainOutput(MediaCodec.BufferInfo info) {
        while (true) {
            int outIndex =
                    codec.dequeueOutputBuffer(info, 0);

            if (outIndex == MediaCodec.INFO_TRY_AGAIN_LATER) {
                break;

            } else if (outIndex ==
                    MediaCodec.INFO_OUTPUT_FORMAT_CHANGED) {

                MediaFormat newFormat = codec.getOutputFormat();
                trackIndex = muxer.addTrack(newFormat);
                muxer.start();
                muxerStarted = true;

                Log.i(TAG, "Muxer started");

            } else if (outIndex >= 0) {

                writeOutput(outIndex, info);

                if ((info.flags &
                        MediaCodec.BUFFER_FLAG_END_OF_STREAM) != 0) {
                    break;
                }
            }
        }
    }

    private void drainUntilEOS(MediaCodec.BufferInfo info) {
        while (true) {
            int outIndex =
                    codec.dequeueOutputBuffer(info, 10_000);

            if (outIndex ==
                    MediaCodec.INFO_OUTPUT_FORMAT_CHANGED) {

                MediaFormat newFormat = codec.getOutputFormat();
                trackIndex = muxer.addTrack(newFormat);
                muxer.start();
                muxerStarted = true;

                Log.i(TAG, "Muxer started (EOS)");

            } else if (outIndex >= 0) {

                writeOutput(outIndex, info);

                if ((info.flags &
                        MediaCodec.BUFFER_FLAG_END_OF_STREAM) != 0) {
                    Log.i(TAG, "EOS received, totalEncoded=" + encodedFrameCount);
                    break;
                }
            }
        }
    }

    private void writeOutput(int outIndex, MediaCodec.BufferInfo info) {
        if (muxerStarted && info.size > 0) {
            ByteBuffer out =
                    codec.getOutputBuffer(outIndex);
            if (out != null) {
                out.position(info.offset);
                out.limit(info.offset + info.size);
                
                // ===== SHA256 =====
                if (sha256 != null) {
                    if (out.hasArray()) {
                        // 常见情况：直接用 backing array（零拷贝）
                        sha256.update(
                            out.array(),
                            out.arrayOffset() + out.position(),
                            info.size
                        );
                    } else {
                        // 兜底：DirectBuffer
                        byte[] tmp = new byte[info.size];
                        out.get(tmp);
                        sha256.update(tmp);
                        out.position(info.offset); // 还原给 muxer 用
                    }
                }
                
                muxer.writeSampleData(
                        trackIndex, out, info);

                encodedFrameCount++;
                logEncodeProgress(info);
            }
        }
        codec.releaseOutputBuffer(outIndex, false);
    }

    // ================= logging =================

    private void logEncodeProgress(MediaCodec.BufferInfo info) {
        long now = System.currentTimeMillis();
        if (now - lastLogTimeMs > 1000) {
            lastLogTimeMs = now;
            Log.i(TAG,
                    "EncodedFrames=" + encodedFrameCount +
                    " ptsUs=" + info.presentationTimeUs +
                    " size=" + info.size +
                    " flags=" + info.flags +
                    " queue=" + frameQueue.size());
        }
    }

    private void release() {
        try {
            codec.stop();
            codec.release();
        } catch (Exception ignored) {}

        try {
            if (muxerStarted) 
            {
                muxerStarted = false;
                muxer.stop();
            }
            muxer.release();
        } catch (Exception ignored) {}
        
        if (sha256 != null) {
            byte[] hash = sha256.digest();
            StringBuilder sb = new StringBuilder();
            for (byte b : hash) {
                sb.append(String.format("%02x", b));
            }
            finalSha256 = sb.toString();
        }

        Log.i(TAG,
                "Encoder released, totalEncodedFrames=" + encodedFrameCount);
    }
}
