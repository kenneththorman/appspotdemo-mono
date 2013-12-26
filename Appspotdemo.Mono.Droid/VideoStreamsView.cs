/*
 * libjingle
 * Copyright 2013, Google Inc.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *
 *  1. Redistributions of source code must retain the above copyright notice,
 *     this list of conditions and the following disclaimer.
 *  2. Redistributions in binary form must reproduce the above copyright notice,
 *     this list of conditions and the following disclaimer in the documentation
 *     and/or other materials provided with the distribution.
 *  3. The name of the author may not be used to endorse or promote products
 *     derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE AUTHOR ``AS IS'' AND ANY EXPRESS OR IMPLIED
 * WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF
 * MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO
 * EVENT SHALL THE AUTHOR BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
 * SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO,
 * PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS;
 * OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY,
 * WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR
 * OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF
 * ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System.Collections.Generic;
using Android.Content;
using Android.Graphics;
using Android.Opengl;
using Android.Util;
using Java.Lang;
using Java.Nio;
using Javax.Microedition.Khronos.Egl;
using Javax.Microedition.Khronos.Opengles;
using Org.Webrtc;
using Exception = System.Exception;


namespace Appspotdemo.Mono.Droid
{
	/// <summary>
	/// A GLSurfaceView{,.Renderer} that efficiently renders YUV frames from local &
	/// remote VideoTracks using the GPU for CSC.  Clients will want to call the
	/// constructor, setSize() and updateFrame() as appropriate, but none of the
	/// other public methods of this class are of interest to clients (only to system
	/// classes).
	/// </summary>
	public class VideoStreamsView : GLSurfaceView, GLSurfaceView.IRenderer
	{

	  /// <summary>
	  /// Identify which of the two video streams is being addressed. </summary>
	  public enum Endpoint
	  {
		  LOCAL,
		  REMOTE
	  }

	  private const string TAG = "VideoStreamsView";
	  private Dictionary<Endpoint, Rect> rects = new Dictionary<Endpoint, Rect>();
	  private Point screenDimensions;
	  // [0] are local Y,U,V, [1] are remote Y,U,V.
	  private int[][] yuvTextures = new int[][] {new int[] {-1, -1, -1}, new int[] {-1, -1, -1}};
	  private int posLocation = -1;
	  private long lastFPSLogTime = Java.Lang.JavaSystem.NanoTime();
	  private long numFramesSinceLastLog = 0;
	  private FramePool framePool = new FramePool();
	  // Accessed on multiple threads!  Must be synchronized.
	  private Dictionary<Endpoint, Org.Webrtc.VideoRenderer.I420Frame> framesToRender = new Dictionary<Endpoint, Org.Webrtc.VideoRenderer.I420Frame>();

	  public VideoStreamsView(Context c, Point screenDimensions) : base(c)
	  {
		this.screenDimensions = screenDimensions;
		PreserveEGLContextOnPause = true;
		SetEGLContextClientVersion(2);
		SetRenderer(this);
		RenderMode = Rendermode.WhenDirty;
	  }

	  /// <summary>
	  /// Queue |frame| to be uploaded. </summary>
//JAVA TO C# CONVERTER WARNING: 'final' parameters are not allowed in .NET:
//ORIGINAL LINE: public void queueFrame(final Endpoint stream, org.webrtc.VideoRenderer.I420Frame frame)
	  public virtual void queueFrame(Endpoint stream, Org.Webrtc.VideoRenderer.I420Frame frame)
	  {
		// Paying for the copy of the YUV data here allows CSC and painting time
		// to get spent on the render thread instead of the UI thread.
		abortUnless(FramePool.validateDimensions(frame), "Frame too large!");
//JAVA TO C# CONVERTER WARNING: The original Java variable was marked 'final':
//ORIGINAL LINE: final org.webrtc.VideoRenderer.I420Frame frameCopy = framePool.takeFrame(frame).copyFrom(frame);
		VideoRenderer.I420Frame frameCopy = framePool.takeFrame(frame).CopyFrom(frame);
		bool needToScheduleRender;
		lock (framesToRender)
		{
		  // A new render needs to be scheduled (via updateFrames()) iff there isn't
		  // already a render scheduled, which is true iff framesToRender is empty.
		  needToScheduleRender = framesToRender.Count == 0;
		  framesToRender.Add(stream, frameCopy);
		  if (frameCopy != null)
		  {
			  framePool.returnFrame(frameCopy);
		  }
		}
		if (needToScheduleRender)
		{
		  QueueEvent(new RunnableAnonymousInnerClassHelper(this));
		}
	  }

	  private class RunnableAnonymousInnerClassHelper : Object, IRunnable
	  {
		  private readonly VideoStreamsView outerInstance;

		  public RunnableAnonymousInnerClassHelper(VideoStreamsView outerInstance)
		  {
			  this.outerInstance = outerInstance;
		  }

		  public void Run()
		  {
			outerInstance.updateFrames();
		  }
	  }

	  // Upload the planes from |framesToRender| to the textures owned by this View.
	  private void updateFrames()
	  {
		VideoRenderer.I420Frame localFrame = null;
		VideoRenderer.I420Frame remoteFrame = null;
		lock (framesToRender)
		{
			framesToRender.TryGetValue(Endpoint.LOCAL, out localFrame);
			framesToRender.Remove(Endpoint.LOCAL);
			framesToRender.TryGetValue(Endpoint.REMOTE, out remoteFrame);
			framesToRender.Remove(Endpoint.REMOTE);
		}
		if (localFrame != null)
		{
		  texImage2D(localFrame, yuvTextures[0]);
		  framePool.returnFrame(localFrame);
		}
		if (remoteFrame != null)
		{
		  texImage2D(remoteFrame, yuvTextures[1]);
		  framePool.returnFrame(remoteFrame);
		}
		abortUnless(localFrame != null || remoteFrame != null, "Nothing to render!");
		RequestRender();
	  }

	  /// <summary>
	  /// Inform this View of the dimensions of frames coming from |stream|. </summary>
	  public virtual void setSize(Endpoint stream, int width, int height)
	  {
		// Generate 3 texture ids for Y/U/V and place them into |textures|,
		// allocating enough storage for |width|x|height| pixels.
		int[] textures = yuvTextures[stream == Endpoint.LOCAL ? 0 : 1];
		GLES20.GlGenTextures(3, textures, 0);
		for (int i = 0; i < 3; ++i)
		{
		  int w = i == 0 ? width : width / 2;
		  int h = i == 0 ? height : height / 2;
		  GLES20.GlActiveTexture(GLES20.GlTexture0 + i);
		  GLES20.GlBindTexture(GLES20.GlTexture2d, textures[i]);
		  GLES20.GlTexImage2D(GLES20.GlTexture2d, 0, GLES20.GlLuminance, w, h, 0, GLES20.GlLuminance, GLES20.GlUnsignedByte, null);
		  GLES20.GlTexParameterf(GLES20.GlTexture2d, GLES20.GlTextureMinFilter, GLES20.GlLinear);
		  GLES20.GlTexParameterf(GLES20.GlTexture2d, GLES20.GlTextureMagFilter, GLES20.GlLinear);
		  GLES20.GlTexParameterf(GLES20.GlTexture2d, GLES20.GlTextureWrapS, GLES20.GlClampToEdge);
		  GLES20.GlTexParameterf(GLES20.GlTexture2d, GLES20.GlTextureWrapT, GLES20.GlClampToEdge);
		}
		checkNoGLES2Error();
	  }

	  protected internal void OnMeasure(int unusedX, int unusedY)
	  {
		// Go big or go home!
		SetMeasuredDimension(screenDimensions.X, screenDimensions.Y);
	  }

	  public void OnSurfaceChanged(IGL10 unused, int width, int height)
	  {
		GLES20.GlViewport(0, 0, width, height);
		checkNoGLES2Error();
	  }

	  public void OnDrawFrame(IGL10 unused)
	  {
		GLES20.GlClear(GLES20.GlColorBufferBit);
		drawRectangle(yuvTextures[1], remoteVertices);
		drawRectangle(yuvTextures[0], localVertices);
		++numFramesSinceLastLog;
		long now = JavaSystem.NanoTime();
		if (lastFPSLogTime == -1 || now - lastFPSLogTime > 1e9)
		{
		  double fps = numFramesSinceLastLog / ((now - lastFPSLogTime) / 1e9);
		  Log.Debug(TAG, "Rendered FPS: " + fps);
		  lastFPSLogTime = now;
		  numFramesSinceLastLog = 1;
		}
		checkNoGLES2Error();
	  }

	  public void OnSurfaceCreated(IGL10 unused, EGLConfig config)
	  {
		int program = GLES20.GlCreateProgram();
		addShaderTo(GLES20.GlVertexShader, VERTEX_SHADER_STRING, program);
		addShaderTo(GLES20.GlFragmentShader, FRAGMENT_SHADER_STRING, program);

		GLES20.GlLinkProgram(program);
		int[] result = new int[] {GLES20.GlFalse};
		result[0] = GLES20.GlFalse;
		GLES20.GlGetProgramiv(program, GLES20.GlLinkStatus, result, 0);
		abortUnless(result[0] == GLES20.GlTrue, GLES20.GlGetProgramInfoLog(program));
		GLES20.GlUseProgram(program);

		GLES20.GlUniform1i(GLES20.GlGetUniformLocation(program, "y_tex"), 0);
		GLES20.GlUniform1i(GLES20.GlGetUniformLocation(program, "u_tex"), 1);
		GLES20.GlUniform1i(GLES20.GlGetUniformLocation(program, "v_tex"), 2);

		// Actually set in drawRectangle(), but queried only once here.
		posLocation = GLES20.GlGetAttribLocation(program, "in_pos");

		int tcLocation = GLES20.GlGetAttribLocation(program, "in_tc");
		GLES20.GlEnableVertexAttribArray(tcLocation);
		GLES20.GlVertexAttribPointer(tcLocation, 2, GLES20.GlFloat, false, 0, textureCoords);

		GLES20.GlClearColor(0.0f, 0.0f, 0.0f, 1.0f);
		checkNoGLES2Error();
	  }

	  // Wrap a float[] in a direct FloatBuffer using native byte order.
	  private static FloatBuffer directNativeFloatBuffer(float[] array)
	  {
		FloatBuffer buffer = ByteBuffer.AllocateDirect(array.Length * 4).Order(ByteOrder.NativeOrder()).AsFloatBuffer();
		buffer.Put(array);
		buffer.Flip();
		return buffer;
	  }

	  // Upload the YUV planes from |frame| to |textures|.
	  private void texImage2D(VideoRenderer.I420Frame frame, int[] textures)
	  {
		for (int i = 0; i < 3; ++i)
		{
		  ByteBuffer plane = frame.YuvPlanes[i];
		  GLES20.GlActiveTexture(GLES20.GlTexture0 + i);
		  GLES20.GlBindTexture(GLES20.GlTexture2d, textures[i]);
		  int w = i == 0 ? frame.Width : frame.Width / 2;
		  int h = i == 0 ? frame.Height : frame.Height / 2;
		  abortUnless(w == frame.YuvStrides[i], frame.YuvStrides[i] + "!=" + w);
		  GLES20.GlTexImage2D(GLES20.GlTexture2d, 0, GLES20.GlLuminance, w, h, 0, GLES20.GlLuminance, GLES20.GlUnsignedByte, plane);
		}
		checkNoGLES2Error();
	  }

	  // Draw |textures| using |vertices| (X,Y coordinates).
	  private void drawRectangle(int[] textures, FloatBuffer vertices)
	  {
		for (int i = 0; i < 3; ++i)
		{
		  GLES20.GlActiveTexture(GLES20.GlTexture0 + i);
		  GLES20.GlBindTexture(GLES20.GlTexture2d, textures[i]);
		}

		GLES20.GlVertexAttribPointer(posLocation, 2, GLES20.GlFloat, false, 0, vertices);
		GLES20.GlEnableVertexAttribArray(posLocation);

		GLES20.GlDrawArrays(GLES20.GlTriangleStrip, 0, 4);
		checkNoGLES2Error();
	  }

	  // Compile & attach a |type| shader specified by |source| to |program|.
	  private static void addShaderTo(int type, string source, int program)
	  {
		int[] result = new int[] {GLES20.GlFalse};
		int shader = GLES20.GlCreateShader(type);
		GLES20.GlShaderSource(shader, source);
		GLES20.GlCompileShader(shader);
		GLES20.GlGetShaderiv(shader, GLES20.GlCompileStatus, result, 0);
		abortUnless(result[0] == GLES20.GlTrue, GLES20.GlGetShaderInfoLog(shader) + ", source: " + source);
		GLES20.GlAttachShader(program, shader);
		GLES20.GlDeleteShader(shader);
		checkNoGLES2Error();
	  }

	  // Poor-man's assert(): die with |msg| unless |condition| is true.
	  private static void abortUnless(bool condition, string msg)
	  {
		if (!condition)
		{
		  throw new Exception(msg);
		}
	  }

	  // Assert that no OpenGL ES 2.0 error has been raised.
	  private static void checkNoGLES2Error()
	  {
		int error = GLES20.GlGetError();
		abortUnless(error == GLES20.GlNoError, "GLES20 error: " + error);
	  }

	  // Remote image should span the full screen.
	  private static readonly FloatBuffer remoteVertices = directNativeFloatBuffer(new float[] {-1, 1, -1, -1, 1, 1, 1, -1});

	  // Local image should be thumbnailish.
	  private static readonly FloatBuffer localVertices = directNativeFloatBuffer(new float[] {0.6f, 0.9f, 0.6f, 0.6f, 0.9f, 0.9f, 0.9f, 0.6f});

	  // Texture Coordinates mapping the entire texture.
	  private static readonly FloatBuffer textureCoords = directNativeFloatBuffer(new float[] {0, 0, 0, 1, 1, 0, 1, 1});

	  // Pass-through vertex shader.
	  private const string VERTEX_SHADER_STRING = "varying vec2 interp_tc;\n" + "\n" + "attribute vec4 in_pos;\n" + "attribute vec2 in_tc;\n" + "\n" + "void main() {\n" + "  gl_Position = in_pos;\n" + "  interp_tc = in_tc;\n" + "}\n";

	  // YUV to RGB pixel shader. Loads a pixel from each plane and pass through the
	  // matrix.
	  private const string FRAGMENT_SHADER_STRING = "precision mediump float;\n" + "varying vec2 interp_tc;\n" + "\n" + "uniform sampler2D y_tex;\n" + "uniform sampler2D u_tex;\n" + "uniform sampler2D v_tex;\n" + "\n" + "void main() {\n" + "  float y = texture2D(y_tex, interp_tc).r;\n" + "  float u = texture2D(u_tex, interp_tc).r - .5;\n" + "  float v = texture2D(v_tex, interp_tc).r - .5;\n" + "  gl_FragColor = vec4(y + 1.403 * v, " + "                      y - 0.344 * u - 0.714 * v, " + "                      y + 1.77 * u, 1);\n" + "}\n";
		  // CSC according to http://www.fourcc.org/fccyvrgb.php
	}

}