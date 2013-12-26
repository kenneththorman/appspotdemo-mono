using System.Collections.Generic;
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
using System.Linq;
using Org.Webrtc;

namespace Appspotdemo.Mono.Droid
{
	/// <summary>
	/// This class acts as an allocation pool meant to minimize GC churn caused by
	/// frame allocation & disposal.  The public API comprises of just two methods:
	/// copyFrame(), which allocates as necessary and copies, and
	/// returnFrame(), which returns frame ownership to the pool for use by a later
	/// call to copyFrame().
	/// 
	/// This class is thread-safe; calls to copyFrame() and returnFrame() are allowed
	/// to happen on any thread.
	/// </summary>
	internal class FramePool
	{
	  // Maps each summary code (see summarizeFrameDimensions()) to a list of frames
	  // of that description.
	  private readonly Dictionary<long, Stack<VideoRenderer.I420Frame>> availableFrames = new Dictionary<long, Stack<VideoRenderer.I420Frame>>();
	  // Every dimension (e.g. width, height, stride) of a frame must be less than
	  // this value.
	  private const long MAX_DIMENSION = 4096;

	  public virtual VideoRenderer.I420Frame takeFrame(VideoRenderer.I420Frame source)
	  {
		long desc = summarizeFrameDimensions(source);
		VideoRenderer.I420Frame dst = null;
		lock (availableFrames)
		{
		  Stack<VideoRenderer.I420Frame> frames = availableFrames[desc];
		  if (frames == null)
		  {
			frames = new Stack<VideoRenderer.I420Frame>();
			availableFrames[desc] = frames;
		  }
		  if (frames.Count > 0)
		  {
			dst = frames.Pop();
		  }
		  else
		  {
			dst = new VideoRenderer.I420Frame(source.Width, source.Height, source.YuvStrides.ToArray(), null);
		  }
		}
		return dst;
	  }

	  public virtual void returnFrame(VideoRenderer.I420Frame frame)
	  {
		long desc = summarizeFrameDimensions(frame);
		lock (availableFrames)
		{
		  Stack<VideoRenderer.I420Frame> frames = availableFrames[desc];
		  if (frames == null)
		  {
			throw new System.ArgumentException("Unexpected frame dimensions");
		  }
		  frames.Push(frame);
		}
	  }

	  /// <summary>
	  /// Validate that |frame| can be managed by the pool. </summary>
	  public static bool validateDimensions(VideoRenderer.I420Frame frame)
	  {
		return frame.Width < MAX_DIMENSION && frame.Height < MAX_DIMENSION && frame.YuvStrides[0] < MAX_DIMENSION && frame.YuvStrides[1] < MAX_DIMENSION && frame.YuvStrides[2] < MAX_DIMENSION;
	  }

	  // Return a code summarizing the dimensions of |frame|.  Two frames that
	  // return the same summary are guaranteed to be able to store each others'
	  // contents.  Used like Object.hashCode(), but we need all the bits of a long
	  // to do a good job, and hashCode() returns int, so we do this.
	  private static long summarizeFrameDimensions(VideoRenderer.I420Frame frame)
	  {
		long ret = frame.Width;
		ret = ret * MAX_DIMENSION + frame.Height;
		ret = ret * MAX_DIMENSION + frame.YuvStrides[0];
		ret = ret * MAX_DIMENSION + frame.YuvStrides[1];
		ret = ret * MAX_DIMENSION + frame.YuvStrides[2];
		return ret;
	  }
	}

}