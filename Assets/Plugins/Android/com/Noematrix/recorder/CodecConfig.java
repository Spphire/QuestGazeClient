package com.Noematrix.recorder;

/**
 * 纯配置类（不参与任何逻辑）
 */
public final class CodecConfig {

    public static final String MIME_TYPE = "video/avc";
    public static final int I_FRAME_INTERVAL = 1;
    public static final int BITRATE_PER_PIXEL = 6;

    private CodecConfig() {}
}
