using System;
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
	using Log = android.util.Log;
	using TypedValue = android.util.TypedValue;
	using ScrollView = android.widget.ScrollView;
	using TextView = android.widget.TextView;


	/// <summary>
	/// Singleton helper: install a default unhandled exception handler which shows
	/// an informative dialog and kills the app.  Useful for apps whose
	/// error-handling consists of throwing RuntimeExceptions.
	/// NOTE: almost always more useful to
	/// Thread.setDefaultUncaughtExceptionHandler() rather than
	/// Thread.setUncaughtExceptionHandler(), to apply to background threads as well.
	/// </summary>
	public class UnhandledExceptionHandler : System.Threading.Thread.UncaughtExceptionHandler
	{
	  private const string TAG = "AppRTCDemoActivity";
	  private readonly Activity activity;

//JAVA TO C# CONVERTER WARNING: 'final' parameters are not allowed in .NET:
//ORIGINAL LINE: public UnhandledExceptionHandler(final android.app.Activity activity)
	  public UnhandledExceptionHandler(Activity activity)
	  {
		this.activity = activity;
	  }

//JAVA TO C# CONVERTER WARNING: 'final' parameters are not allowed in .NET:
//ORIGINAL LINE: public void uncaughtException(Thread unusedThread, final Throwable e)
	  public virtual void uncaughtException(Thread unusedThread, Exception e)
	  {
		activity.runOnUiThread(new RunnableAnonymousInnerClassHelper(this, e));
	  }

	  private class RunnableAnonymousInnerClassHelper : Runnable
	  {
		  private readonly UnhandledExceptionHandler outerInstance;

		  private Exception e;

		  public RunnableAnonymousInnerClassHelper(UnhandledExceptionHandler outerInstance, Exception e)
		  {
			  this.outerInstance = outerInstance;
			  this.e = e;
		  }

		  public override void run()
		  {
			string title = "Fatal error: " + getTopLevelCauseMessage(e);
			string msg = getRecursiveStackTrace(e);
			TextView errorView = new TextView(outerInstance.activity);
			errorView.Text = msg;
			errorView.setTextSize(TypedValue.COMPLEX_UNIT_SP, 8);
			ScrollView scrollingContainer = new ScrollView(outerInstance.activity);
			scrollingContainer.addView(errorView);
			Log.e(TAG, title + "\n\n" + msg);
			DialogInterface.OnClickListener listener = new OnClickListenerAnonymousInnerClassHelper(this);
			AlertDialog.Builder builder = new AlertDialog.Builder(outerInstance.activity);
			builder.setTitle(title).setView(scrollingContainer).setPositiveButton("Exit", listener).show();
		  }

		  private class OnClickListenerAnonymousInnerClassHelper : DialogInterface.OnClickListener
		  {
			  private readonly RunnableAnonymousInnerClassHelper outerInstance;

			  public OnClickListenerAnonymousInnerClassHelper(RunnableAnonymousInnerClassHelper outerInstance)
			  {
				  this.outerInstance = outerInstance;
			  }

			  public override void onClick(DialogInterface dialog, int which)
			  {
				dialog.dismiss();
				Environment.Exit(1);
			  }
		  }
	  }

	  // Returns the Message attached to the original Cause of |t|.
	  private static string getTopLevelCauseMessage(Exception t)
	  {
		Exception topLevelCause = t;
		while (topLevelCause.InnerException != null)
		{
		  topLevelCause = topLevelCause.InnerException;
		}
		return topLevelCause.Message;
	  }

	  // Returns a human-readable String of the stacktrace in |t|, recursively
	  // through all Causes that led to |t|.
	  private static string getRecursiveStackTrace(Exception t)
	  {
		StringWriter writer = new StringWriter();
		t.printStackTrace(new PrintWriter(writer));
		return writer.ToString();
	  }
	}

}