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

	using SuppressLint = android.annotation.SuppressLint;
	using Activity = android.app.Activity;
	using Log = android.util.Log;
	using ConsoleMessage = android.webkit.ConsoleMessage;
	using JavascriptInterface = android.webkit.JavascriptInterface;
	using WebChromeClient = android.webkit.WebChromeClient;
	using WebView = android.webkit.WebView;
	using WebViewClient = android.webkit.WebViewClient;

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
//JAVA TO C# CONVERTER TODO TASK: Most Java annotations will not have direct .NET equivalent attributes:
//ORIGINAL LINE: @SuppressLint("SetJavaScriptEnabled") public GAEChannelClient(android.app.Activity activity, String token, MessageHandler handler)
	  public GAEChannelClient(Activity activity, string token, MessageHandler handler)
	  {
		webView = new WebView(activity);
		webView.Settings.JavaScriptEnabled = true;
		webView.WebChromeClient = new WebChromeClientAnonymousInnerClassHelper(this); // Purely for debugging.
		webView.WebViewClient = new WebViewClientAnonymousInnerClassHelper(this); // Purely for debugging.
		proxyingMessageHandler = new ProxyingMessageHandler(activity, handler, token);
		webView.addJavascriptInterface(proxyingMessageHandler, "androidMessageHandler");
		webView.loadUrl("file:///android_asset/channel.html");
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
			Log.d(TAG, "console: " + msg.message() + " at " + msg.sourceId() + ":" + msg.lineNumber());
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
			Log.e(TAG, "JS error: " + errorCode + " in " + failingUrl + ", desc: " + description);
		  }
	  }

	  /// <summary>
	  /// Close the connection to the AppEngine channel. </summary>
	  public virtual void close()
	  {
		if (webView == null)
		{
		  return;
		}
		proxyingMessageHandler.disconnect();
		webView.removeJavascriptInterface("androidMessageHandler");
		webView.loadUrl("about:blank");
		webView = null;
	  }

	  // Helper class for proxying callbacks from the Java<->JS interaction
	  // (private, background) thread to the Activity's UI thread.
	  private class ProxyingMessageHandler
	  {
		internal readonly Activity activity;
		internal readonly MessageHandler handler;
		internal readonly bool[] disconnected_Renamed = new bool[] {false};
		internal readonly string token;

		public ProxyingMessageHandler(Activity activity, MessageHandler handler, string token)
		{
		  this.activity = activity;
		  this.handler = handler;
		  this.token = token;
		}

		public virtual void disconnect()
		{
		  disconnected_Renamed[0] = true;
		}

		internal virtual bool disconnected()
		{
		  return disconnected_Renamed[0];
		}

//JAVA TO C# CONVERTER TODO TASK: Most Java annotations will not have direct .NET equivalent attributes:
//ORIGINAL LINE: @JavascriptInterface public String getToken()
		public virtual string Token
		{
			get
			{
			  return token;
			}
		}

//JAVA TO C# CONVERTER TODO TASK: Most Java annotations will not have direct .NET equivalent attributes:
//ORIGINAL LINE: @JavascriptInterface public void onOpen()
		public virtual void onOpen()
		{
		  activity.runOnUiThread(new RunnableAnonymousInnerClassHelper(this));
		}

		private class RunnableAnonymousInnerClassHelper : Runnable
		{
			private readonly ProxyingMessageHandler outerInstance;

			public RunnableAnonymousInnerClassHelper(ProxyingMessageHandler outerInstance)
			{
				this.outerInstance = outerInstance;
			}

			public virtual void run()
			{
			  if (!outerInstance.disconnected())
			  {
				outerInstance.handler.onOpen();
			  }
			}
		}

//JAVA TO C# CONVERTER TODO TASK: Most Java annotations will not have direct .NET equivalent attributes:
//ORIGINAL LINE: @JavascriptInterface public void onMessage(final String data)
//JAVA TO C# CONVERTER WARNING: 'final' parameters are not allowed in .NET:
		public virtual void onMessage(string data)
		{
		  activity.runOnUiThread(new RunnableAnonymousInnerClassHelper2(this, data));
		}

		private class RunnableAnonymousInnerClassHelper2 : Runnable
		{
			private readonly ProxyingMessageHandler outerInstance;

			private string data;

			public RunnableAnonymousInnerClassHelper2(ProxyingMessageHandler outerInstance, string data)
			{
				this.outerInstance = outerInstance;
				this.data = data;
			}

			public virtual void run()
			{
			  if (!outerInstance.disconnected())
			  {
				outerInstance.handler.onMessage(data);
			  }
			}
		}

//JAVA TO C# CONVERTER TODO TASK: Most Java annotations will not have direct .NET equivalent attributes:
//ORIGINAL LINE: @JavascriptInterface public void onClose()
		public virtual void onClose()
		{
		  activity.runOnUiThread(new RunnableAnonymousInnerClassHelper3(this));
		}

		private class RunnableAnonymousInnerClassHelper3 : Runnable
		{
			private readonly ProxyingMessageHandler outerInstance;

			public RunnableAnonymousInnerClassHelper3(ProxyingMessageHandler outerInstance)
			{
				this.outerInstance = outerInstance;
			}

			public virtual void run()
			{
			  if (!outerInstance.disconnected())
			  {
				outerInstance.handler.onClose();
			  }
			}
		}

//JAVA TO C# CONVERTER TODO TASK: Most Java annotations will not have direct .NET equivalent attributes:
//ORIGINAL LINE: @JavascriptInterface public void onError(final int code, final String description)
//JAVA TO C# CONVERTER WARNING: 'final' parameters are not allowed in .NET:
		public virtual void onError(int code, string description)
		{
		  activity.runOnUiThread(new RunnableAnonymousInnerClassHelper4(this, code, description));
		}

		private class RunnableAnonymousInnerClassHelper4 : Runnable
		{
			private readonly ProxyingMessageHandler outerInstance;

			private int code;
			private string description;

			public RunnableAnonymousInnerClassHelper4(ProxyingMessageHandler outerInstance, int code, string description)
			{
				this.outerInstance = outerInstance;
				this.code = code;
				this.description = description;
			}

			public virtual void run()
			{
			  if (!outerInstance.disconnected())
			  {
				outerInstance.handler.onError(code, description);
			  }
			}
		}
	  }
	}

}