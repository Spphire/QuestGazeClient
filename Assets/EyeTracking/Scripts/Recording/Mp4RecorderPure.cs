using System;
using System.IO;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace EyeTracking.Recording
{
    public sealed class Mp4RecorderPure : IDisposable
    {
        private const int PipelineDepth = 30;
        private const int JobBatchSize = 256;

        private readonly int recorderId;
        private readonly int width;
        private readonly int height;
        private readonly int fps;
        private readonly string videoFileName;
        private readonly bool flipVertical;

#if UNITY_ANDROID && !UNITY_EDITOR
        private readonly AndroidJavaClass mp4RecorderList;
#endif

        private struct JobSlot
        {
            public NativeArray<byte> rgba;
            public NativeArray<byte> nv12;
            public JobHandle handle;
            public long ptsUs;
            public bool inUse;
        }

        private readonly JobSlot[] slots;
        private readonly byte[] nv12Managed;
        private Texture2D readbackTexture;
        private int writeIndex;
        private int readIndex;
        private long frameIndex;
        private int pushedFrameCount;
        private bool recording;

        public string FinalSha256 { get; private set; }

        public Mp4RecorderPure(int recorderId, int width, int height, int fps, string videoFileName, bool flipVertical)
        {
            this.recorderId = recorderId;
            this.width = width & ~1;
            this.height = height & ~1;
            this.fps = fps;
            this.videoFileName = videoFileName;
            this.flipVertical = flipVertical;

            int rgbaSize = this.width * this.height * 4;
            int nv12Size = this.width * this.height * 3 / 2;

            slots = new JobSlot[PipelineDepth];
            for (int i = 0; i < PipelineDepth; i++)
            {
                slots[i].rgba = new NativeArray<byte>(rgbaSize, Allocator.Persistent);
                slots[i].nv12 = new NativeArray<byte>(nv12Size, Allocator.Persistent);
                slots[i].inUse = false;
            }

            nv12Managed = new byte[nv12Size];

#if UNITY_ANDROID && !UNITY_EDITOR
            mp4RecorderList = new AndroidJavaClass("com.Noematrix.recorder.Mp4RecorderList");
#endif
        }

        public void StartRecording(string outputDirectory)
        {
            if (recording)
            {
                return;
            }

            Directory.CreateDirectory(outputDirectory);

            writeIndex = 0;
            readIndex = 0;
            frameIndex = 0;
            pushedFrameCount = 0;
            FinalSha256 = null;

            string mp4Path = Path.Combine(outputDirectory, videoFileName);
#if UNITY_ANDROID && !UNITY_EDITOR
            mp4RecorderList.CallStatic("start", recorderId, mp4Path, width, height, fps);
#endif
            recording = true;
        }

        public void RecordFrame(Texture texture)
        {
            if (!recording || texture == null)
            {
                return;
            }

            Texture2D texture2D = TextureToReadableTexture2D(texture);
            if (texture2D != null)
            {
                ScheduleFrame(texture2D);
                FlushCompletedFrame();
            }
        }

        public void StopRecording()
        {
            if (!recording)
            {
                return;
            }

            recording = false;
            FlushAllFrames();

#if UNITY_ANDROID && !UNITY_EDITOR
            FinalSha256 = pushedFrameCount > 0
                ? mp4RecorderList.CallStatic<string>("stop", recorderId)
                : null;
#endif
        }

        private void ScheduleFrame(Texture2D texture)
        {
            JobSlot slot = slots[writeIndex];
            if (slot.inUse)
            {
                return;
            }

            var raw = texture.GetRawTextureData<byte>();
            if (raw.Length != slot.rgba.Length)
            {
                Debug.LogWarning($"[Mp4RecorderPure] Skip frame: expected {slot.rgba.Length} RGBA bytes, got {raw.Length}.");
                return;
            }

            NativeArray<byte>.Copy(raw, slot.rgba);
            slot.ptsUs = frameIndex * 1_000_000L / fps;
            slot.handle = new RgbaToNv12Job
            {
                rgba = slot.rgba,
                nv12 = slot.nv12,
                width = width,
                height = height,
                flipVertical = flipVertical
            }.Schedule(width * height, JobBatchSize);

            slot.inUse = true;
            slots[writeIndex] = slot;

            writeIndex = (writeIndex + 1) % PipelineDepth;
            frameIndex++;
        }

        private Texture2D TextureToReadableTexture2D(Texture texture)
        {
            if (texture is Texture2D texture2D)
            {
                return texture2D;
            }

            if (texture is not RenderTexture renderTexture)
            {
                Debug.LogWarning($"[Mp4RecorderPure] Skip frame: unsupported texture type {texture.GetType().Name}.");
                return null;
            }

            if (readbackTexture == null ||
                readbackTexture.width != width ||
                readbackTexture.height != height)
            {
                if (readbackTexture != null)
                {
                    UnityEngine.Object.Destroy(readbackTexture);
                }

                readbackTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            }

            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = renderTexture;
                readbackTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                readbackTexture.Apply(false, false);
                return readbackTexture;
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }

        private void FlushCompletedFrame()
        {
            JobSlot front = slots[readIndex];
            if (!front.inUse || !front.handle.IsCompleted)
            {
                return;
            }

            PushFrame(front);
            front.inUse = false;
            slots[readIndex] = front;
            readIndex = (readIndex + 1) % PipelineDepth;
        }

        private void FlushAllFrames()
        {
            for (int i = 0; i < PipelineDepth; i++)
            {
                JobSlot slot = slots[readIndex];
                if (slot.inUse)
                {
                    PushFrame(slot);
                    slot.inUse = false;
                    slots[readIndex] = slot;
                }

                readIndex = (readIndex + 1) % PipelineDepth;
            }
        }

        private void PushFrame(JobSlot slot)
        {
            slot.handle.Complete();
            slot.nv12.CopyTo(nv12Managed);

#if UNITY_ANDROID && !UNITY_EDITOR
            var signedBuffer = new sbyte[nv12Managed.Length];
            Buffer.BlockCopy(nv12Managed, 0, signedBuffer, 0, nv12Managed.Length);
            mp4RecorderList.CallStatic("pushFrame", recorderId, signedBuffer, slot.ptsUs);
#endif
            pushedFrameCount++;
        }

        public void Dispose()
        {
            StopRecording();

            if (slots == null)
            {
                return;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].rgba.IsCreated)
                {
                    slots[i].rgba.Dispose();
                }

                if (slots[i].nv12.IsCreated)
                {
                    slots[i].nv12.Dispose();
                }
            }

            if (readbackTexture != null)
            {
                UnityEngine.Object.Destroy(readbackTexture);
                readbackTexture = null;
            }
        }
    }
}
