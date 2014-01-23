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

using Android.App;
using Android.Util;
using Android.Webkit;

namespace Appspotdemo.Mono.Droid
{
	/// <summary>
	/// Java-land version of Google AppEngine's JavaScript Channel API:
	/// https://developers.google.com/appengine/docs/python/channel/javascript
	/// 
	/// Requires a hosted HTML page that opens the desired channel and dispatches JS
	/// on{Open,Message,Close,Error}() events to a global object named
	/// "androidMessageHandler".
	/// </summary>
	public class GAEChannelClient
	{
		private const string TAG = "GAEChannelClient";
		private WebView webView;
		private readonly ProxyingMessageHandler proxyingMessageHandler;

		/// <summary>
		/// Callback interface for messages delivered on the Google AppEngine channel.
		/// 
		/// Methods are guaranteed to be invoked on the UI thread of |activity| passed
		/// to GAEChannelClient's constructor.
		/// </summary>
		public interface MessageHandler
		{
			void onOpen();
			void onMessage(string data);
			void onClose();
			void onError(int code, string description);
		}

		/// <summary>
		/// Asynchronously open an AppEngine channel. </summary>
		public GAEChannelClient(Activity activity, string token, MessageHandler handler)
		{
			webView = new WebView(activity);
			webView.Settings.JavaScriptEnabled = true;
			webView.SetWebChromeClient(new WebChromeClientAnonymousInnerClassHelper(this)); // Purely for debugging.
			webView.SetWebViewClient(new WebViewClientAnonymousInnerClassHelper(this)); // Purely for debugging.
			Log.Debug(TAG,string.Format("token:{0}", token));
			proxyingMessageHandler = new ProxyingMessageHandler(activity, handler, token);
			webView.AddJavascriptInterface(proxyingMessageHandler, "androidMessageHandler");
			webView.LoadUrl("file:///android_asset/channel.html");
		}

		private class WebChromeClientAnonymousInnerClassHelper : WebChromeClient
		{
			private readonly GAEChannelClient outerInstance;

			public WebChromeClientAnonymousInnerClassHelper(GAEChannelClient outerInstance)
			{
				this.outerInstance = outerInstance;
			}

			public virtual bool onConsoleMessage(ConsoleMessage msg)
			{
				Log.Debug(TAG, "console: " + msg.Message() + " at " + msg.SourceId() + ":" + msg.LineNumber());
				return false;
			}
		}

		private class WebViewClientAnonymousInnerClassHelper : WebViewClient
		{
			private readonly GAEChannelClient outerInstance;

			public WebViewClientAnonymousInnerClassHelper(GAEChannelClient outerInstance)
			{
				this.outerInstance = outerInstance;
			}

			public virtual void onReceivedError(WebView view, int errorCode, string description, string failingUrl)
			{
				Log.Error(TAG, "JS error: " + errorCode + " in " + failingUrl + ", desc: " + description);
			}
		}

		/// <summary>
		/// Close the connection to the AppEngine channel. </summary>
		public void close()
		{
			if (webView == null)
			{
				return;
			}
			proxyingMessageHandler.disconnect();
			webView.RemoveJavascriptInterface("androidMessageHandler");
			webView.LoadUrl("about:blank");
			webView = null;
		}

		// Helper class for proxying callbacks from the Java<->JS interaction
		// (private, background) thread to the Activity's UI thread.
		private class ProxyingMessageHandler : Java.Lang.Object
		{
			private readonly Activity activity;
			private readonly MessageHandler handler;
			private readonly bool[] _disconnected = { false };
			private readonly Java.Lang.String token;

			public ProxyingMessageHandler(Activity activity, MessageHandler handler, string token)
			{
				this.activity = activity;
				this.handler = handler;
				this.token = new Java.Lang.String(token);
			}

			public void disconnect()
			{
				_disconnected[0] = true;
			}

			private bool disconnected()
			{
				return _disconnected[0];
			}

			[JavascriptInterface]
			public Java.Lang.String getToken()
			{
				return token;
			}

			[JavascriptInterface]
			public void onOpen()
			{
				activity.RunOnUiThread(() => handler.onOpen());
			}


			[JavascriptInterface]
			public void onMessage(string data)
			{
				activity.RunOnUiThread(() => handler.onMessage(data));
			}


			[JavascriptInterface]
			public void onClose()
			{
				activity.RunOnUiThread(() => handler.onClose());
			}

			[JavascriptInterface]
			public void onError(int code, Java.Lang.String description)
			{
				activity.RunOnUiThread(() => handler.onError(code, (string)description));
			}
		}
	}
}