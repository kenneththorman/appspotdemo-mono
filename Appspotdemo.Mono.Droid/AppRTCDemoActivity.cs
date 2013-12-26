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
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Media;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;
using Java.Lang;
using Java.Util.Regex;
using Org.Json;
using Org.Webrtc;
using Exception = System.Exception;
using Pattern = Android.OS.Pattern;
using StringBuilder = System.Text.StringBuilder;
using Thread = System.Threading.Thread;
using VideoSource = Android.Media.VideoSource;

namespace Appspotdemo.Mono.Droid
{
	/// <summary>
	/// Main Activity of the AppRTCDemo Android app demonstrating interoperability
	/// between the Android/Java implementation of PeerConnection and the
	/// apprtc.appspot.com demo webapp.
	/// </summary>
	[Activity(Label = "Appspotdemo.Mono.Droid", MainLauncher = true, Icon = "@drawable/ic_launcher")]
	public class AppRTCDemoActivity : Activity, AppRTCClient.IceServersObserver
	{
		private bool InstanceFieldsInitialized = false;

		public AppRTCDemoActivity()
		{
			if (!InstanceFieldsInitialized)
			{
				InitializeInstanceFields();
				InstanceFieldsInitialized = true;
			}
		}

		private void InitializeInstanceFields()
		{
			pcObserver = new PCObserver(this);
			sdpObserver = new SDPObserver(this);
			gaeHandler = new GAEHandler(this);
			appRtcClient = new AppRTCClient(this, gaeHandler, this);
		}

	  private const string TAG = "AppRTCDemoActivity";
	  private PeerConnectionFactory factory;
	  private Org.Webrtc.VideoSource videoSource;
	  private PeerConnection pc;
	  private PCObserver pcObserver;
	  private SDPObserver sdpObserver;
	  private GAEChannelClient.MessageHandler gaeHandler;
	  private AppRTCClient appRtcClient;
	  private VideoStreamsView vsv;
	  private Toast logToast;
	  private LinkedList<IceCandidate> queuedRemoteCandidates = new LinkedList<IceCandidate>();
	  // Synchronize on quit[0] to avoid teardown-related crashes.
	  private readonly Boolean[] quit = new Boolean[] { Boolean.False };
	  private MediaConstraints sdpMediaConstraints;

	  protected override void OnCreate(Bundle savedInstanceState)
	  {
		base.OnCreate(savedInstanceState);

		Java.Lang.Thread.DefaultUncaughtExceptionHandler = new UnhandledExceptionHandler(this);

		Window.AddFlags(WindowManagerFlags.Fullscreen);
		Window.AddFlags(WindowManagerFlags.KeepScreenOn);

		Point displaySize = new Point();
		WindowManager.DefaultDisplay.GetSize(displaySize);
		vsv = new VideoStreamsView(this, displaySize);
		SetContentView(vsv);

		abortUnless(PeerConnectionFactory.InitializeAndroidGlobals(this), "Failed to initializeAndroidGlobals");

		AudioManager audioManager = ((AudioManager) GetSystemService(AudioService));
		// TODO(fischman): figure out how to do this Right(tm) and remove the
		// suppression.
		bool isWiredHeadsetOn = audioManager.WiredHeadsetOn;
		audioManager.Mode = isWiredHeadsetOn ? Mode.InCall: Mode.InCommunication;
		audioManager.SpeakerphoneOn = !isWiredHeadsetOn;

		sdpMediaConstraints = new MediaConstraints();
		sdpMediaConstraints.Mandatory.Add(new MediaConstraints.KeyValuePair("OfferToReceiveAudio", "true"));
		sdpMediaConstraints.Mandatory.Add(new MediaConstraints.KeyValuePair("OfferToReceiveVideo", "true"));

		Intent intent = Intent;
		if ("Android.intent.action.VIEW".Equals(intent.Action))
		{
		  connectToRoom(intent.Data.ToString());
		  return;
		}
		showGetRoomUI();
	  }

	  private void showGetRoomUI()
	  {
		EditText roomInput = new EditText(this);
		roomInput.Text = "https://apprtc.appspot.com/?r=";
		roomInput.SetSelection(roomInput.Text.Length);
		IDialogInterfaceOnClickListener listener = new OnClickListenerAnonymousInnerClassHelper(this, roomInput);
		AlertDialog.Builder builder = new AlertDialog.Builder(this);
		builder.SetMessage("Enter room URL").SetView(roomInput).SetPositiveButton("Go!", listener).Show();
	  }

	  private class OnClickListenerAnonymousInnerClassHelper : Java.Lang.Object, IDialogInterfaceOnClickListener
	  {
		  private readonly AppRTCDemoActivity outerInstance;

		  private EditText roomInput;

		  public OnClickListenerAnonymousInnerClassHelper(AppRTCDemoActivity outerInstance, EditText roomInput)
		  {
			  this.outerInstance = outerInstance;
			  this.roomInput = roomInput;
		  }

		  public void OnClick(IDialogInterface dialog, int which)
		  {
			abortUnless(which == (int)DialogInterface.ButtonPositive, "lolwat?");
			dialog.Dismiss();
			outerInstance.connectToRoom(roomInput.Text.ToString());
		  }
	  }

	  private void connectToRoom(string roomUrl)
	  {
		logAndToast("Connecting to room...");
		appRtcClient.connectToRoom(roomUrl);
	  }

	  protected override void OnPause()
	  {
		base.OnPause();
		vsv.OnPause();
		if (videoSource != null)
		{
		  videoSource.Stop();
		}
	  }

	  protected override void OnResume()
	  {
		base.OnResume();
		vsv.OnResume();
		if (videoSource != null)
		{
		  videoSource.Restart();
		}
	  }

	  public void onIceServers(IList<PeerConnection.IceServer> iceServers)
	  {
		factory = new PeerConnectionFactory();
		pc = factory.CreatePeerConnection(iceServers, appRtcClient.pcConstraints(), pcObserver);

		// Uncomment to get ALL WebRTC tracing and SENSITIVE libjingle logging.
		// NOTE: this _must_ happen while |factory| is alive!
		// Logging.enableTracing(
		//     "logcat:",
		//     EnumSet.of(Logging.TraceLevel.TRACE_ALL),
		//     Logging.Severity.LS_SENSITIVE);

		{
		  PeerConnection finalPC = pc;
//JAVA TO C# CONVERTER WARNING: The original Java variable was marked 'final':
//ORIGINAL LINE: final Runnable repeatedStatsLogger = new Runnable()
		  IRunnable repeatedStatsLogger = new RunnableAnonymousInnerClassHelper(this, finalPC);
		  vsv.PostDelayed(repeatedStatsLogger, 10000);
		}

		{
		  logAndToast("Creating local video source...");
		  MediaStream lMS = factory.CreateLocalMediaStream("ARDAMS");
		  if (appRtcClient.videoConstraints() != null)
		  {
			VideoCapturer capturer = VideoCapturer;
			videoSource = factory.CreateVideoSource(capturer, appRtcClient.videoConstraints());
			VideoTrack videoTrack = factory.CreateVideoTrack("ARDAMSv0", videoSource);
			videoTrack.AddRenderer(new VideoRenderer(new VideoCallbacks(this, vsv, VideoStreamsView.Endpoint.LOCAL)));
			lMS.AddTrack(videoTrack);
		  }
		  lMS.AddTrack(factory.CreateAudioTrack("ARDAMSa0"));
		  pc.AddStream(lMS, new MediaConstraints());
		}
		logAndToast("Waiting for ICE candidates...");
	  }

	  private class RunnableAnonymousInnerClassHelper : Java.Lang.Object, IRunnable
	  {
		  private readonly AppRTCDemoActivity outerInstance;

		  private PeerConnection finalPC;

		  public RunnableAnonymousInnerClassHelper(AppRTCDemoActivity outerInstance, PeerConnection finalPC)
		  {
			  this.outerInstance = outerInstance;
			  this.finalPC = finalPC;
		  }

		  public void Run()
		  {
			lock (outerInstance.quit[0])
			{
			  if (outerInstance.quit[0]==Boolean.True)
			  {
				return;
			  }
			  IRunnable runnableThis = this;
			  bool success = finalPC.GetStats(new StatsObserverAnonymousInnerClassHelper(this, runnableThis), null);
			  if (!success)
			  {
				throw new Exception("getStats() return false!");
			  }
			}
		  }

		  private class StatsObserverAnonymousInnerClassHelper : Java.Lang.Object, IStatsObserver
		  {
			  private readonly RunnableAnonymousInnerClassHelper outerInstance;

			  private IRunnable runnableThis;

			  public StatsObserverAnonymousInnerClassHelper(RunnableAnonymousInnerClassHelper outerInstance, IRunnable runnableThis)
			  {
				  this.outerInstance = outerInstance;
				  this.runnableThis = runnableThis;
			  }

			  public void OnComplete(StatsReport[] reports)
			  {
				foreach (StatsReport report in reports)
				{
				  Log.Debug(TAG, "Stats: " + report.ToString());
				}
				outerInstance.outerInstance.vsv.PostDelayed(runnableThis, 10000);
			  }
		  }
	  }

	  // Cycle through likely device names for the camera and return the first
	  // capturer that works, or crash if none do.
	  private VideoCapturer VideoCapturer
	  {
		  get
		  {
			string[] cameraFacing = new string[] {"front", "back"};
			int[] cameraIndex = new int[] {0, 1};
			int[] cameraOrientation = new int[] {0, 90, 180, 270};
			foreach (string facing in cameraFacing)
			{
			  foreach (int index in cameraIndex)
			  {
				foreach (int orientation in cameraOrientation)
				{
				  string name = "Camera " + index + ", Facing " + facing + ", Orientation " + orientation;
				  VideoCapturer capturer = VideoCapturer.Create(name);
				  if (capturer != null)
				  {
					logAndToast("Using camera: " + name);
					return capturer;
				  }
				}
			  }
			}
			throw new Exception("Failed to open capturer");
		  }
	  }

	  protected internal void OnDestroy()
	  {
		disconnectAndExit();
		base.OnDestroy();
	  }

	  // Poor-man's assert(): die with |msg| unless |condition| is true.
	  private static void abortUnless(bool condition, string msg)
	  {
		if (!condition)
		{
		  throw new Exception(msg);
		}
	  }

	  // Log |msg| and Toast about it.
	  private void logAndToast(string msg)
	  {
		Log.Debug(TAG, msg);
		if (logToast != null)
		{
		  logToast.Cancel();
		}
		logToast = Toast.MakeText(this, msg, ToastLength.Short);
		logToast.Show();
	  }

	  // Send |json| to the underlying AppEngine Channel.
	  private void sendMessage(JSONObject json)
	  {
		appRtcClient.sendMessage(json.ToString());
	  }

	  // Put a |key|->|value| mapping in |json|.
	  private static void jsonPut(JSONObject json, string key, Java.Lang.Object value)
	  {
		try
		{
		  json.Put(key, value);
		}
		catch (JSONException e)
		{
		  throw new Exception("Error", e);
		}
	  }

	  // Mangle SDP to prefer ISAC/16000 over any other audio codec.
	  private string preferISAC(string sdpDescription)
	  {
		string[] lines = sdpDescription.Split("\n", true);
		int mLineIndex = -1;
		string isac16kRtpMap = null;
		Java.Util.Regex.Pattern isac16kPattern = Java.Util.Regex.Pattern.Compile("^a=rtpmap:(\\d+) ISAC/16000[\r]?$");
		for (int i = 0; (i < lines.Length) && (mLineIndex == -1 || isac16kRtpMap == null); ++i)
		{
		  if (lines[i].StartsWith("m=audio "))
		  {
			mLineIndex = i;
			continue;
		  }
		  Matcher isac16kMatcher = isac16kPattern.Matcher(lines[i]);
		  if (isac16kMatcher.Matches())
		  {
			isac16kRtpMap = isac16kMatcher.Group(1);
			continue;
		  }
		}
		if (mLineIndex == -1)
		{
		  Log.Debug(TAG, "No m=audio line, so can't prefer iSAC");
		  return sdpDescription;
		}
		if (isac16kRtpMap == null)
		{
		  Log.Debug(TAG, "No ISAC/16000 line, so can't prefer iSAC");
		  return sdpDescription;
		}
		string[] origMLineParts = lines[mLineIndex].Split(" ", true);
		StringBuilder newMLine = new StringBuilder();
		int origPartIndex = 0;
		// Format is: m=<media> <port> <proto> <fmt> ...
		newMLine.Append(origMLineParts[origPartIndex++]).Append(" ");
		newMLine.Append(origMLineParts[origPartIndex++]).Append(" ");
		newMLine.Append(origMLineParts[origPartIndex++]).Append(" ");
		newMLine.Append(isac16kRtpMap).Append(" ");
		for (; origPartIndex < origMLineParts.Length; ++origPartIndex)
		{
		  if (!origMLineParts[origPartIndex].Equals(isac16kRtpMap))
		  {
			newMLine.Append(origMLineParts[origPartIndex]).Append(" ");
		  }
		}
		lines[mLineIndex] = newMLine.ToString();
		StringBuilder newSdpDescription = new StringBuilder();
		foreach (string line in lines)
		{
		  newSdpDescription.Append(line).Append("\n");
		}
		return newSdpDescription.ToString();
	  }

	  // Implementation detail: observe ICE & stream changes and react accordingly.
	  private class PCObserver : Java.Lang.Object, PeerConnection.IObserver
	  {
		  private readonly AppRTCDemoActivity outerInstance;

		  public PCObserver(AppRTCDemoActivity outerInstance)
		  {
			  this.outerInstance = outerInstance;
		  }

		public void OnIceCandidate(IceCandidate candidate)
		{
		  outerInstance.RunOnUiThread(new RunnableAnonymousInnerClassHelper2(this, candidate));
		}

		private class RunnableAnonymousInnerClassHelper2 : Java.Lang.Object, IRunnable
		{
			private readonly PCObserver outerInstance;

			private IceCandidate candidate;

			public RunnableAnonymousInnerClassHelper2(PCObserver outerInstance, IceCandidate candidate)
			{
				this.outerInstance = outerInstance;
				this.candidate = candidate;
			}

			public void Run()
			{
			  JSONObject json = new JSONObject();
			  jsonPut(json, "type", "candidate");
			  jsonPut(json, "label", candidate.SdpMLineIndex);
			  jsonPut(json, "id", candidate.SdpMid);
			  jsonPut(json, "candidate", candidate.Sdp);
			  outerInstance.outerInstance.sendMessage(json);
			}
		}

		public void OnError()
		{
		  outerInstance.RunOnUiThread(new RunnableAnonymousInnerClassHelper3(this));
		}

		private class RunnableAnonymousInnerClassHelper3 : Java.Lang.Object, IRunnable
		{
			private readonly PCObserver outerInstance;

			public RunnableAnonymousInnerClassHelper3(PCObserver outerInstance)
			{
				this.outerInstance = outerInstance;
			}

			public void Run()
			{
			  throw new Exception("PeerConnection error!");
			}
		}

		public void OnSignalingChange(PeerConnection.SignalingState newState)
		{
		}

		public void OnIceConnectionChange(PeerConnection.IceConnectionState newState)
		{
		}

		public void OnIceGatheringChange(PeerConnection.IceGatheringState newState)
		{
		}

		public void OnAddStream(MediaStream stream)
		{
		  outerInstance.RunOnUiThread(new RunnableAnonymousInnerClassHelper4(this, stream));
		}

		private class RunnableAnonymousInnerClassHelper4 : Java.Lang.Object, IRunnable
		{
			private readonly PCObserver outerInstance;

			private MediaStream stream;

			public RunnableAnonymousInnerClassHelper4(PCObserver outerInstance, MediaStream stream)
			{
				this.outerInstance = outerInstance;
				this.stream = stream;
			}

			public void Run()
			{
			  abortUnless(stream.AudioTracks.Size() <= 1 && stream.VideoTracks.Size() <= 1, "Weird-looking stream: " + stream);
			  if (stream.VideoTracks.Size() == 1)
			  {
				((Org.Webrtc.VideoTrack)stream.VideoTracks.Get(0)).AddRenderer(new VideoRenderer(new VideoCallbacks(outerInstance.outerInstance, outerInstance.outerInstance.vsv, VideoStreamsView.Endpoint.REMOTE)));
			  }
			}
		}

		public void OnRemoveStream(MediaStream stream)
		{
		  outerInstance.RunOnUiThread(new RunnableAnonymousInnerClassHelper5(this, stream));
		}

		private class RunnableAnonymousInnerClassHelper5 : Java.Lang.Object, IRunnable
		{
			private readonly PCObserver outerInstance;

			private MediaStream stream;

			public RunnableAnonymousInnerClassHelper5(PCObserver outerInstance, MediaStream stream)
			{
				this.outerInstance = outerInstance;
				this.stream = stream;
			}

			public void Run()
			{
			  stream.VideoTracks.Get(0).Dispose();
			}
		}

		public void OnDataChannel(DataChannel dc)
		{
		  outerInstance.RunOnUiThread(new RunnableAnonymousInnerClassHelper6(this, dc));
		}

		private class RunnableAnonymousInnerClassHelper6 : Java.Lang.Object, IRunnable
		{
			private readonly PCObserver outerInstance;

			private DataChannel dc;

			public RunnableAnonymousInnerClassHelper6(PCObserver outerInstance, DataChannel dc)
			{
				this.outerInstance = outerInstance;
				this.dc = dc;
			}

			public void Run()
			{
			  throw new Exception("AppRTC doesn't use data channels, but got: " + dc.Label() + " anyway!");
			}
		}
	  }

	  private class SDPObserver : Java.Lang.Object, ISdpObserver
	  {
		  private readonly AppRTCDemoActivity outerInstance;

		  public SDPObserver(AppRTCDemoActivity outerInstance)
		  {
			  this.outerInstance = outerInstance;
		  }

		public void OnCreateSuccess(SessionDescription origSdp)
		{
		  outerInstance.RunOnUiThread(new RunnableAnonymousInnerClassHelper(this, origSdp));
		}

		private class RunnableAnonymousInnerClassHelper : Java.Lang.Object, IRunnable
		{
			private readonly SDPObserver outerInstance;

			private SessionDescription origSdp;

			public RunnableAnonymousInnerClassHelper(SDPObserver outerInstance, SessionDescription origSdp)
			{
				this.outerInstance = outerInstance;
				this.origSdp = origSdp;
			}

			public virtual void Run()
			{
			  outerInstance.outerInstance.logAndToast("Sending " + origSdp.Type);
			  SessionDescription sdp = new SessionDescription(origSdp.Type, outerInstance.outerInstance.preferISAC(origSdp.Description));
			  JSONObject json = new JSONObject();
			  jsonPut(json, "type", sdp.Type.CanonicalForm());
			  jsonPut(json, "sdp", sdp.Description);
			  outerInstance.outerInstance.sendMessage(json);
			  outerInstance.outerInstance.pc.SetLocalDescription(outerInstance.outerInstance.sdpObserver, sdp);
			}
		}

		public void OnSetSuccess()
		{
		  outerInstance.RunOnUiThread(new RunnableAnonymousInnerClassHelper2(this));
		}

		private class RunnableAnonymousInnerClassHelper2 : Java.Lang.Object, IRunnable
		{
			private readonly SDPObserver outerInstance;

			public RunnableAnonymousInnerClassHelper2(SDPObserver outerInstance)
			{
				this.outerInstance = outerInstance;
			}

			public virtual void Run()
			{
			  if (outerInstance.outerInstance.appRtcClient.Initiator)
			  {
				if (outerInstance.outerInstance.pc.RemoteDescription != null)
				{
				  // We've set our local offer and received & set the remote
				  // answer, so drain candidates.
				  outerInstance.drainRemoteCandidates();
				}
			  }
			  else
			  {
				if (outerInstance.outerInstance.pc.LocalDescription == null)
				{
				  // We just set the remote offer, time to create our answer.
				  outerInstance.outerInstance.logAndToast("Creating answer");
				  outerInstance.outerInstance.pc.CreateAnswer(outerInstance, outerInstance.outerInstance.sdpMediaConstraints);
				}
				else
				{
				  // Sent our answer and set it as local description; drain
				  // candidates.
				  outerInstance.drainRemoteCandidates();
				}
			  }
			}
		}

		public void OnCreateFailure(string error)
		{
		  outerInstance.RunOnUiThread(new RunnableAnonymousInnerClassHelper3(this, error));
		}

		private class RunnableAnonymousInnerClassHelper3 : Java.Lang.Object, IRunnable
		{
			private readonly SDPObserver outerInstance;

			private string error;

			public RunnableAnonymousInnerClassHelper3(SDPObserver outerInstance, string error)
			{
				this.outerInstance = outerInstance;
				this.error = error;
			}

			public void Run()
			{
			  throw new Exception("createSDP error: " + error);
			}
		}

		public void OnSetFailure(string error)
		{
		  outerInstance.RunOnUiThread(new RunnableAnonymousInnerClassHelper4(this, error));
		}

		private class RunnableAnonymousInnerClassHelper4 : Java.Lang.Object, IRunnable
		{
			private readonly SDPObserver outerInstance;

			private string error;

			public RunnableAnonymousInnerClassHelper4(SDPObserver outerInstance, string error)
			{
				this.outerInstance = outerInstance;
				this.error = error;
			}

			public virtual void Run()
			{
			  throw new Exception("setSDP error: " + error);
			}
		}

		internal virtual void drainRemoteCandidates()
		{
		  foreach (IceCandidate candidate in outerInstance.queuedRemoteCandidates)
		  {
			outerInstance.pc.AddIceCandidate(candidate);
		  }
		  outerInstance.queuedRemoteCandidates = null;
		}
	  }

	  // Implementation detail: handler for receiving GAE messages and dispatching
	  // them appropriately.
	  private class GAEHandler : GAEChannelClient.MessageHandler
	  {
		  private readonly AppRTCDemoActivity outerInstance;

		  public GAEHandler(AppRTCDemoActivity outerInstance)
		  {
			  this.outerInstance = outerInstance;
		  }

//JAVA TO C# CONVERTER TODO TASK: Most Java annotations will not have direct .NET equivalent attributes:
//ORIGINAL LINE: @JavascriptInterface public void onOpen()
		public virtual void onOpen()
		{
		  if (!outerInstance.appRtcClient.Initiator)
		  {
			return;
		  }
		  outerInstance.logAndToast("Creating offer...");
		  outerInstance.pc.CreateOffer(outerInstance.sdpObserver, outerInstance.sdpMediaConstraints);
		}

//JAVA TO C# CONVERTER TODO TASK: Most Java annotations will not have direct .NET equivalent attributes:
//ORIGINAL LINE: @JavascriptInterface public void onMessage(String data)
		public virtual void onMessage(string data)
		{
		  try
		  {
			JSONObject json = new JSONObject(data);
			string type = (string) json.Get("type");
			if (type.Equals("candidate"))
			{
			  IceCandidate candidate = new IceCandidate((string) json.Get("id"), json.GetInt("label"), (string) json.Get("candidate"));
			  if (outerInstance.queuedRemoteCandidates != null)
			  {
				outerInstance.queuedRemoteCandidates.AddLast(candidate);
			  }
			  else
			  {
				outerInstance.pc.AddIceCandidate(candidate);
			  }
			}
			else if (type.Equals("answer") || type.Equals("offer"))
			{
			  SessionDescription sdp = new SessionDescription(SessionDescription.SessionDescriptionType.FromCanonicalForm(type), outerInstance.preferISAC((string) json.Get("sdp")));
			  outerInstance.pc.SetRemoteDescription(outerInstance.sdpObserver, sdp);
			}
			else if (type.Equals("bye"))
			{
			  outerInstance.logAndToast("Remote end hung up; dropping PeerConnection");
			  outerInstance.disconnectAndExit();
			}
			else
			{
			  throw new Exception("Unexpected message: " + data);
			}
		  }
		  catch (JSONException e)
		  {
			throw new Exception("Error", e);
		  }
		}

//JAVA TO C# CONVERTER TODO TASK: Most Java annotations will not have direct .NET equivalent attributes:
//ORIGINAL LINE: @JavascriptInterface public void onClose()
		public virtual void onClose()
		{
		  outerInstance.disconnectAndExit();
		}

//JAVA TO C# CONVERTER TODO TASK: Most Java annotations will not have direct .NET equivalent attributes:
//ORIGINAL LINE: @JavascriptInterface public void onError(int code, String description)
		public virtual void onError(int code, string description)
		{
		  outerInstance.disconnectAndExit();
		}
	  }

	  // Disconnect from remote resources, dispose of local resources, and exit.
	  private void disconnectAndExit()
	  {
		lock (quit[0])
		{
		  if (quit[0]==Boolean.True)
		  {
			return;
		  }
		  quit[0] = Boolean.True;
		  if (pc != null)
		  {
			pc.Dispose();
			pc = null;
		  }
		  if (appRtcClient != null)
		  {
			appRtcClient.sendMessage("{\"type\": \"bye\"}");
			appRtcClient.disconnect();
			appRtcClient = null;
		  }
		  if (videoSource != null)
		  {
			videoSource.Dispose();
			videoSource = null;
		  }
		  if (factory != null)
		  {
			factory.Dispose();
			factory = null;
		  }
		  Finish();
		}
	  }

	  // Implementation detail: bridge the VideoRenderer.Callbacks interface to the
	  // VideoStreamsView implementation.
	  private class VideoCallbacks : Java.Lang.Object, VideoRenderer.ICallbacks
	  {
		  private readonly AppRTCDemoActivity outerInstance;

		internal readonly VideoStreamsView view;
		internal readonly VideoStreamsView.Endpoint stream;

		public VideoCallbacks(AppRTCDemoActivity outerInstance, VideoStreamsView view, VideoStreamsView.Endpoint stream)
		{
			this.outerInstance = outerInstance;
		  this.view = view;
		  this.stream = stream;
		}

//JAVA TO C# CONVERTER WARNING: 'final' parameters are not allowed in .NET:
//ORIGINAL LINE: @Override public void setSize(final int width, final int height)
		public void SetSize(int width, int height)
		{
		  view.QueueEvent(new RunnableAnonymousInnerClassHelper(this, width, height));
		}

		private class RunnableAnonymousInnerClassHelper : Java.Lang.Object, IRunnable
		{
			private readonly VideoCallbacks outerInstance;

			private int width;
			private int height;

			public RunnableAnonymousInnerClassHelper(VideoCallbacks outerInstance, int width, int height)
			{
				this.outerInstance = outerInstance;
				this.width = width;
				this.height = height;
			}

			public void Run()
			{
			  outerInstance.view.setSize(outerInstance.stream, width, height);
			}
		}

		public void RenderFrame(VideoRenderer.I420Frame frame)
		{
		  view.queueFrame(stream, frame);
		}
	  }
	}

}