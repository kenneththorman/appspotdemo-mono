using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

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

namespace org.appspot.apprtc
{

	using Activity = android.app.Activity;
	using AlertDialog = android.app.AlertDialog;
	using DialogInterface = android.content.DialogInterface;
	using Intent = android.content.Intent;
	using Point = android.graphics.Point;
	using AudioManager = android.media.AudioManager;
	using Bundle = android.os.Bundle;
	using Log = android.util.Log;
	using WindowManager = android.view.WindowManager;
	using JavascriptInterface = android.webkit.JavascriptInterface;
	using EditText = android.widget.EditText;
	using Toast = android.widget.Toast;

	using JSONException = org.json.JSONException;
	using JSONObject = org.json.JSONObject;
	using DataChannel = org.webrtc.DataChannel;
	using IceCandidate = org.webrtc.IceCandidate;
	using Logging = org.webrtc.Logging;
	using MediaConstraints = org.webrtc.MediaConstraints;
	using MediaStream = org.webrtc.MediaStream;
	using PeerConnection = org.webrtc.PeerConnection;
	using PeerConnectionFactory = org.webrtc.PeerConnectionFactory;
	using SdpObserver = org.webrtc.SdpObserver;
	using SessionDescription = org.webrtc.SessionDescription;
	using StatsObserver = org.webrtc.StatsObserver;
	using StatsReport = org.webrtc.StatsReport;
	using VideoCapturer = org.webrtc.VideoCapturer;
	using VideoRenderer = org.webrtc.VideoRenderer;
	using I420Frame = org.webrtc.VideoRenderer.I420Frame;
	using VideoSource = org.webrtc.VideoSource;
	using VideoTrack = org.webrtc.VideoTrack;


	/// <summary>
	/// Main Activity of the AppRTCDemo Android app demonstrating interoperability
	/// between the Android/Java implementation of PeerConnection and the
	/// apprtc.appspot.com demo webapp.
	/// </summary>
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
	  private VideoSource videoSource;
	  private PeerConnection pc;
	  private PCObserver pcObserver;
	  private SDPObserver sdpObserver;
	  private GAEChannelClient.MessageHandler gaeHandler;
	  private AppRTCClient appRtcClient;
	  private VideoStreamsView vsv;
	  private Toast logToast;
	  private LinkedList<IceCandidate> queuedRemoteCandidates = new LinkedList<IceCandidate>();
	  // Synchronize on quit[0] to avoid teardown-related crashes.
	  private readonly bool?[] quit = new bool?[] {false};
	  private MediaConstraints sdpMediaConstraints;

	  public override void onCreate(Bundle savedInstanceState)
	  {
		base.onCreate(savedInstanceState);

		Thread.DefaultUncaughtExceptionHandler = new UnhandledExceptionHandler(this);

		Window.addFlags(WindowManager.LayoutParams.FLAG_FULLSCREEN);
		Window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);

		Point displaySize = new Point();
		WindowManager.DefaultDisplay.getSize(displaySize);
		vsv = new VideoStreamsView(this, displaySize);
		ContentView = vsv;

		abortUnless(PeerConnectionFactory.initializeAndroidGlobals(this), "Failed to initializeAndroidGlobals");

		AudioManager audioManager = ((AudioManager) getSystemService(AUDIO_SERVICE));
		// TODO(fischman): figure out how to do this Right(tm) and remove the
		// suppression.
//JAVA TO C# CONVERTER TODO TASK: Most Java annotations will not have direct .NET equivalent attributes:
//ORIGINAL LINE: @SuppressWarnings("deprecation") boolean isWiredHeadsetOn = audioManager.isWiredHeadsetOn();
		bool isWiredHeadsetOn = audioManager.WiredHeadsetOn;
		audioManager.Mode = isWiredHeadsetOn ? AudioManager.MODE_IN_CALL : AudioManager.MODE_IN_COMMUNICATION;
		audioManager.SpeakerphoneOn = !isWiredHeadsetOn;

		sdpMediaConstraints = new MediaConstraints();
		sdpMediaConstraints.mandatory.add(new MediaConstraints.KeyValuePair("OfferToReceiveAudio", "true"));
		sdpMediaConstraints.mandatory.add(new MediaConstraints.KeyValuePair("OfferToReceiveVideo", "true"));

//JAVA TO C# CONVERTER WARNING: The original Java variable was marked 'final':
//ORIGINAL LINE: final android.content.Intent intent = getIntent();
		Intent intent = Intent;
		if ("android.intent.action.VIEW".Equals(intent.Action))
		{
		  connectToRoom(intent.Data.ToString());
		  return;
		}
		showGetRoomUI();
	  }

	  private void showGetRoomUI()
	  {
//JAVA TO C# CONVERTER WARNING: The original Java variable was marked 'final':
//ORIGINAL LINE: final android.widget.EditText roomInput = new android.widget.EditText(this);
		EditText roomInput = new EditText(this);
		roomInput.Text = "https://apprtc.appspot.com/?r=";
		roomInput.Selection = roomInput.Text.length();
		DialogInterface.OnClickListener listener = new OnClickListenerAnonymousInnerClassHelper(this, roomInput);
		AlertDialog.Builder builder = new AlertDialog.Builder(this);
		builder.setMessage("Enter room URL").setView(roomInput).setPositiveButton("Go!", listener).show();
	  }

	  private class OnClickListenerAnonymousInnerClassHelper : DialogInterface.OnClickListener
	  {
		  private readonly AppRTCDemoActivity outerInstance;

		  private EditText roomInput;

		  public OnClickListenerAnonymousInnerClassHelper(AppRTCDemoActivity outerInstance, EditText roomInput)
		  {
			  this.outerInstance = outerInstance;
			  this.roomInput = roomInput;
		  }

		  public override void onClick(DialogInterface dialog, int which)
		  {
			abortUnless(which == DialogInterface.BUTTON_POSITIVE, "lolwat?");
			dialog.dismiss();
			outerInstance.connectToRoom(roomInput.Text.ToString());
		  }
	  }

	  private void connectToRoom(string roomUrl)
	  {
		logAndToast("Connecting to room...");
		appRtcClient.connectToRoom(roomUrl);
	  }

	  public override void onPause()
	  {
		base.onPause();
		vsv.onPause();
		if (videoSource != null)
		{
		  videoSource.stop();
		}
	  }

	  public override void onResume()
	  {
		base.onResume();
		vsv.onResume();
		if (videoSource != null)
		{
		  videoSource.restart();
		}
	  }

	  public override void onIceServers(IList<PeerConnection.IceServer> iceServers)
	  {
		factory = new PeerConnectionFactory();
		pc = factory.createPeerConnection(iceServers, appRtcClient.pcConstraints(), pcObserver);

		// Uncomment to get ALL WebRTC tracing and SENSITIVE libjingle logging.
		// NOTE: this _must_ happen while |factory| is alive!
		// Logging.enableTracing(
		//     "logcat:",
		//     EnumSet.of(Logging.TraceLevel.TRACE_ALL),
		//     Logging.Severity.LS_SENSITIVE);

		{
//JAVA TO C# CONVERTER WARNING: The original Java variable was marked 'final':
//ORIGINAL LINE: final org.webrtc.PeerConnection finalPC = pc;
		  PeerConnection finalPC = pc;
//JAVA TO C# CONVERTER WARNING: The original Java variable was marked 'final':
//ORIGINAL LINE: final Runnable repeatedStatsLogger = new Runnable()
		  Runnable repeatedStatsLogger = new RunnableAnonymousInnerClassHelper(this, finalPC);
		  vsv.postDelayed(repeatedStatsLogger, 10000);
		}

		{
		  logAndToast("Creating local video source...");
		  MediaStream lMS = factory.createLocalMediaStream("ARDAMS");
		  if (appRtcClient.videoConstraints() != null)
		  {
			VideoCapturer capturer = VideoCapturer;
			videoSource = factory.createVideoSource(capturer, appRtcClient.videoConstraints());
			VideoTrack videoTrack = factory.createVideoTrack("ARDAMSv0", videoSource);
			videoTrack.addRenderer(new VideoRenderer(new VideoCallbacks(this, vsv, VideoStreamsView.Endpoint.LOCAL)));
			lMS.addTrack(videoTrack);
		  }
		  lMS.addTrack(factory.createAudioTrack("ARDAMSa0"));
		  pc.addStream(lMS, new MediaConstraints());
		}
		logAndToast("Waiting for ICE candidates...");
	  }

	  private class RunnableAnonymousInnerClassHelper : Runnable
	  {
		  private readonly AppRTCDemoActivity outerInstance;

		  private PeerConnection finalPC;

		  public RunnableAnonymousInnerClassHelper(AppRTCDemoActivity outerInstance, PeerConnection finalPC)
		  {
			  this.outerInstance = outerInstance;
			  this.finalPC = finalPC;
		  }

		  public virtual void run()
		  {
			lock (outerInstance.quit[0])
			{
			  if (outerInstance.quit[0])
			  {
				return;
			  }
//JAVA TO C# CONVERTER WARNING: The original Java variable was marked 'final':
//ORIGINAL LINE: final Runnable runnableThis = this;
			  Runnable runnableThis = this;
			  bool success = finalPC.getStats(new StatsObserverAnonymousInnerClassHelper(this, runnableThis), null);
			  if (!success)
			  {
				throw new Exception("getStats() return false!");
			  }
			}
		  }

		  private class StatsObserverAnonymousInnerClassHelper : StatsObserver
		  {
			  private readonly RunnableAnonymousInnerClassHelper outerInstance;

			  private Runnable runnableThis;

			  public StatsObserverAnonymousInnerClassHelper(RunnableAnonymousInnerClassHelper outerInstance, Runnable runnableThis)
			  {
				  this.outerInstance = outerInstance;
				  this.runnableThis = runnableThis;
			  }

			  public virtual void onComplete(StatsReport[] reports)
			  {
				foreach (StatsReport report in reports)
				{
				  Log.d(TAG, "Stats: " + report.ToString());
				}
				outerInstance.outerInstance.vsv.postDelayed(runnableThis, 10000);
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
				  VideoCapturer capturer = VideoCapturer.create(name);
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

	  protected internal override void onDestroy()
	  {
		disconnectAndExit();
		base.onDestroy();
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
		Log.d(TAG, msg);
		if (logToast != null)
		{
		  logToast.cancel();
		}
		logToast = Toast.makeText(this, msg, Toast.LENGTH_SHORT);
		logToast.show();
	  }

	  // Send |json| to the underlying AppEngine Channel.
	  private void sendMessage(JSONObject json)
	  {
		appRtcClient.sendMessage(json.ToString());
	  }

	  // Put a |key|->|value| mapping in |json|.
	  private static void jsonPut(JSONObject json, string key, object value)
	  {
		try
		{
		  json.put(key, value);
		}
		catch (JSONException e)
		{
		  throw new Exception(e);
		}
	  }

	  // Mangle SDP to prefer ISAC/16000 over any other audio codec.
	  private string preferISAC(string sdpDescription)
	  {
		string[] lines = sdpDescription.Split("\n", true);
		int mLineIndex = -1;
		string isac16kRtpMap = null;
		Pattern isac16kPattern = Pattern.compile("^a=rtpmap:(\\d+) ISAC/16000[\r]?$");
		for (int i = 0; (i < lines.Length) && (mLineIndex == -1 || isac16kRtpMap == null); ++i)
		{
		  if (lines[i].StartsWith("m=audio "))
		  {
			mLineIndex = i;
			continue;
		  }
		  Matcher isac16kMatcher = isac16kPattern.matcher(lines[i]);
		  if (isac16kMatcher.matches())
		  {
			isac16kRtpMap = isac16kMatcher.group(1);
			continue;
		  }
		}
		if (mLineIndex == -1)
		{
		  Log.d(TAG, "No m=audio line, so can't prefer iSAC");
		  return sdpDescription;
		}
		if (isac16kRtpMap == null)
		{
		  Log.d(TAG, "No ISAC/16000 line, so can't prefer iSAC");
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
	  private class PCObserver : PeerConnection.Observer
	  {
		  private readonly AppRTCDemoActivity outerInstance;

		  public PCObserver(AppRTCDemoActivity outerInstance)
		  {
			  this.outerInstance = outerInstance;
		  }

//JAVA TO C# CONVERTER WARNING: 'final' parameters are not allowed in .NET:
//ORIGINAL LINE: @Override public void onIceCandidate(final org.webrtc.IceCandidate candidate)
		public override void onIceCandidate(IceCandidate candidate)
		{
		  runOnUiThread(new RunnableAnonymousInnerClassHelper2(this, candidate));
		}

		private class RunnableAnonymousInnerClassHelper2 : Runnable
		{
			private readonly PCObserver outerInstance;

			private IceCandidate candidate;

			public RunnableAnonymousInnerClassHelper2(PCObserver outerInstance, IceCandidate candidate)
			{
				this.outerInstance = outerInstance;
				this.candidate = candidate;
			}

			public virtual void run()
			{
			  JSONObject json = new JSONObject();
			  jsonPut(json, "type", "candidate");
			  jsonPut(json, "label", candidate.sdpMLineIndex);
			  jsonPut(json, "id", candidate.sdpMid);
			  jsonPut(json, "candidate", candidate.sdp);
			  outerInstance.outerInstance.sendMessage(json);
			}
		}

		public override void onError()
		{
		  runOnUiThread(new RunnableAnonymousInnerClassHelper3(this));
		}

		private class RunnableAnonymousInnerClassHelper3 : Runnable
		{
			private readonly PCObserver outerInstance;

			public RunnableAnonymousInnerClassHelper3(PCObserver outerInstance)
			{
				this.outerInstance = outerInstance;
			}

			public virtual void run()
			{
			  throw new Exception("PeerConnection error!");
			}
		}

		public override void onSignalingChange(PeerConnection.SignalingState newState)
		{
		}

		public override void onIceConnectionChange(PeerConnection.IceConnectionState newState)
		{
		}

		public override void onIceGatheringChange(PeerConnection.IceGatheringState newState)
		{
		}

//JAVA TO C# CONVERTER WARNING: 'final' parameters are not allowed in .NET:
//ORIGINAL LINE: @Override public void onAddStream(final org.webrtc.MediaStream stream)
		public override void onAddStream(MediaStream stream)
		{
		  runOnUiThread(new RunnableAnonymousInnerClassHelper4(this, stream));
		}

		private class RunnableAnonymousInnerClassHelper4 : Runnable
		{
			private readonly PCObserver outerInstance;

			private MediaStream stream;

			public RunnableAnonymousInnerClassHelper4(PCObserver outerInstance, MediaStream stream)
			{
				this.outerInstance = outerInstance;
				this.stream = stream;
			}

			public virtual void run()
			{
			  abortUnless(stream.audioTracks.size() <= 1 && stream.videoTracks.size() <= 1, "Weird-looking stream: " + stream);
			  if (stream.videoTracks.size() == 1)
			  {
				stream.videoTracks.get(0).addRenderer(new VideoRenderer(new VideoCallbacks(outerInstance.outerInstance, outerInstance.outerInstance.vsv, VideoStreamsView.Endpoint.REMOTE)));
			  }
			}
		}

//JAVA TO C# CONVERTER WARNING: 'final' parameters are not allowed in .NET:
//ORIGINAL LINE: @Override public void onRemoveStream(final org.webrtc.MediaStream stream)
		public override void onRemoveStream(MediaStream stream)
		{
		  runOnUiThread(new RunnableAnonymousInnerClassHelper5(this, stream));
		}

		private class RunnableAnonymousInnerClassHelper5 : Runnable
		{
			private readonly PCObserver outerInstance;

			private MediaStream stream;

			public RunnableAnonymousInnerClassHelper5(PCObserver outerInstance, MediaStream stream)
			{
				this.outerInstance = outerInstance;
				this.stream = stream;
			}

			public virtual void run()
			{
			  stream.videoTracks.get(0).dispose();
			}
		}

//JAVA TO C# CONVERTER WARNING: 'final' parameters are not allowed in .NET:
//ORIGINAL LINE: @Override public void onDataChannel(final org.webrtc.DataChannel dc)
		public override void onDataChannel(DataChannel dc)
		{
		  runOnUiThread(new RunnableAnonymousInnerClassHelper6(this, dc));
		}

		private class RunnableAnonymousInnerClassHelper6 : Runnable
		{
			private readonly PCObserver outerInstance;

			private DataChannel dc;

			public RunnableAnonymousInnerClassHelper6(PCObserver outerInstance, DataChannel dc)
			{
				this.outerInstance = outerInstance;
				this.dc = dc;
			}

			public virtual void run()
			{
			  throw new Exception("AppRTC doesn't use data channels, but got: " + dc.label() + " anyway!");
			}
		}
	  }

	  // Implementation detail: handle offer creation/signaling and answer setting,
	  // as well as adding remote ICE candidates once the answer SDP is set.
	  private class SDPObserver : SdpObserver
	  {
		  private readonly AppRTCDemoActivity outerInstance;

		  public SDPObserver(AppRTCDemoActivity outerInstance)
		  {
			  this.outerInstance = outerInstance;
		  }

//JAVA TO C# CONVERTER WARNING: 'final' parameters are not allowed in .NET:
//ORIGINAL LINE: @Override public void onCreateSuccess(final org.webrtc.SessionDescription origSdp)
		public override void onCreateSuccess(SessionDescription origSdp)
		{
		  runOnUiThread(new RunnableAnonymousInnerClassHelper(this, origSdp));
		}

		private class RunnableAnonymousInnerClassHelper : Runnable
		{
			private readonly SDPObserver outerInstance;

			private SessionDescription origSdp;

			public RunnableAnonymousInnerClassHelper(SDPObserver outerInstance, SessionDescription origSdp)
			{
				this.outerInstance = outerInstance;
				this.origSdp = origSdp;
			}

			public virtual void run()
			{
			  outerInstance.outerInstance.logAndToast("Sending " + origSdp.type);
			  SessionDescription sdp = new SessionDescription(origSdp.type, outerInstance.outerInstance.preferISAC(origSdp.description));
			  JSONObject json = new JSONObject();
			  jsonPut(json, "type", sdp.type.canonicalForm());
			  jsonPut(json, "sdp", sdp.description);
			  outerInstance.outerInstance.sendMessage(json);
			  outerInstance.outerInstance.pc.setLocalDescription(outerInstance.outerInstance.sdpObserver, sdp);
			}
		}

		public override void onSetSuccess()
		{
		  runOnUiThread(new RunnableAnonymousInnerClassHelper2(this));
		}

		private class RunnableAnonymousInnerClassHelper2 : Runnable
		{
			private readonly SDPObserver outerInstance;

			public RunnableAnonymousInnerClassHelper2(SDPObserver outerInstance)
			{
				this.outerInstance = outerInstance;
			}

			public virtual void run()
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
				  outerInstance.outerInstance.pc.createAnswer(outerInstance, outerInstance.outerInstance.sdpMediaConstraints);
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

//JAVA TO C# CONVERTER WARNING: 'final' parameters are not allowed in .NET:
//ORIGINAL LINE: @Override public void onCreateFailure(final String error)
		public override void onCreateFailure(string error)
		{
		  runOnUiThread(new RunnableAnonymousInnerClassHelper3(this, error));
		}

		private class RunnableAnonymousInnerClassHelper3 : Runnable
		{
			private readonly SDPObserver outerInstance;

			private string error;

			public RunnableAnonymousInnerClassHelper3(SDPObserver outerInstance, string error)
			{
				this.outerInstance = outerInstance;
				this.error = error;
			}

			public virtual void run()
			{
			  throw new Exception("createSDP error: " + error);
			}
		}

//JAVA TO C# CONVERTER WARNING: 'final' parameters are not allowed in .NET:
//ORIGINAL LINE: @Override public void onSetFailure(final String error)
		public override void onSetFailure(string error)
		{
		  runOnUiThread(new RunnableAnonymousInnerClassHelper4(this, error));
		}

		private class RunnableAnonymousInnerClassHelper4 : Runnable
		{
			private readonly SDPObserver outerInstance;

			private string error;

			public RunnableAnonymousInnerClassHelper4(SDPObserver outerInstance, string error)
			{
				this.outerInstance = outerInstance;
				this.error = error;
			}

			public virtual void run()
			{
			  throw new Exception("setSDP error: " + error);
			}
		}

		internal virtual void drainRemoteCandidates()
		{
		  foreach (IceCandidate candidate in outerInstance.queuedRemoteCandidates)
		  {
			outerInstance.pc.addIceCandidate(candidate);
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
		  outerInstance.pc.createOffer(outerInstance.sdpObserver, outerInstance.sdpMediaConstraints);
		}

//JAVA TO C# CONVERTER TODO TASK: Most Java annotations will not have direct .NET equivalent attributes:
//ORIGINAL LINE: @JavascriptInterface public void onMessage(String data)
		public virtual void onMessage(string data)
		{
		  try
		  {
			JSONObject json = new JSONObject(data);
			string type = (string) json.get("type");
			if (type.Equals("candidate"))
			{
			  IceCandidate candidate = new IceCandidate((string) json.get("id"), json.getInt("label"), (string) json.get("candidate"));
			  if (outerInstance.queuedRemoteCandidates != null)
			  {
				outerInstance.queuedRemoteCandidates.AddLast(candidate);
			  }
			  else
			  {
				outerInstance.pc.addIceCandidate(candidate);
			  }
			}
			else if (type.Equals("answer") || type.Equals("offer"))
			{
			  SessionDescription sdp = new SessionDescription(SessionDescription.Type.fromCanonicalForm(type), outerInstance.preferISAC((string) json.get("sdp")));
			  outerInstance.pc.setRemoteDescription(outerInstance.sdpObserver, sdp);
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
			throw new Exception(e);
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
		  if (quit[0])
		  {
			return;
		  }
		  quit[0] = true;
		  if (pc != null)
		  {
			pc.dispose();
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
			videoSource.dispose();
			videoSource = null;
		  }
		  if (factory != null)
		  {
			factory.dispose();
			factory = null;
		  }
		  finish();
		}
	  }

	  // Implementation detail: bridge the VideoRenderer.Callbacks interface to the
	  // VideoStreamsView implementation.
	  private class VideoCallbacks : VideoRenderer.Callbacks
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
		public override void setSize(int width, int height)
		{
		  view.queueEvent(new RunnableAnonymousInnerClassHelper(this, width, height));
		}

		private class RunnableAnonymousInnerClassHelper : Runnable
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

			public virtual void run()
			{
			  outerInstance.view.setSize(outerInstance.stream, width, height);
			}
		}

		public override void renderFrame(VideoRenderer.I420Frame frame)
		{
		  view.queueFrame(stream, frame);
		}
	  }
	}

}