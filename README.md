appspotdemo-mono
================

The AppSpot webrtc demo converted from Java to C#

#FAQ

#1
Have you considered wrapping in a WebView for making the WebRTC client connection and have it coordinate with the main container C# app (stuff like this http://docs.xamarin.com/recipes/android/controls/webview/call_c#_from_javascript/). It seems like that would allow the hard stuff to be done by the chrome on android webview. Doesn't help on the iOS side since its not supported in ios webviews, so you'd be back to native libraries there.

Answer:
Yes I did consider this (if I understand you correctly), and decided against it. I have in the past done webview native integration which at the basic level is not hard to do. However the webview currently supported on Android does not support WebRTC.

According to https://developers.google.com/chrome/mobile/docs/webview/overview they shipped a new version of the webview in Adnroid 4.4 which is built on top of Chrome - yes was my immediate thoughts when I read this, but then later rereading it it states

For the most part, features that work in Chrome for Android should work in the new WebView. Chrome for Android supports a few features which aren't enabled in the WebView, including:WebGL 3D canvas, WebRTC, WebAudio, Fullscreen API, Form validation




#2
I looked at the other repo (https://github.com/kenneththorman/webrtc-app-mono) you have there. It appears that you've moved away from the first approach of porting the WebRTCDemo over to instead porting the libjingle sample that works with the apprtc site. So...I'm still absorbing all this but my understanding is that libjingle takes you down an XMPP approach for signaling, as it includes an implementation of a STUN and relay server. Whereas the first approach would allow you to have a native WebRTC library but you would separately pull in something like rfc5766-turn-server as a STUN/TURN server to use. That allows you to decide separately how you talk to your client apps. Does that sound about right?

Answer:
The AppRtcDemo is using the Google App Engine to handle the signaling, but the code leaves the signaling up to the developer and it is there in the code to change. My basic reason was that when I got the WebRTCDemo UI working/compiling, the UI looked like nothing I had ever seen before and what I needed was basically a native component that pretty much acted and behaved like the apprtc.appspot.com website.

When I built the native .so (http://kenneththorman.blogspot.dk/2014/01/webrtc-app-c-xamarin-part-1-building.html) it also build a full android apk that you can install and test. That was the app I already tried on my devices which worked the same way as apprtc.appspot.com. That that was the code I was actually looking for, and as embarrassing it is to admit it - the repo https://github.com/kenneththorman/webrtc-app-mono was me porting the "wrong" Java app code. When I found out and figured out how to work with JNI (mainly through using the Java Binding Library) I went looking in the official WebRTC code again to find the app that I tested in the Java version. This is the repo at https://github.com/kenneththorman/appspotdemo-mono. So basically having 2 repos are proof of me not being familiar with the official WebRTC code base and not really knowing what code is which :)




#Alternatives to porting to C-sharp?
As an alternative would be to keep the official https://github.com/kenneththorman/appspotdemo-mono app in Java install it side by side with your mono version and make sure it accepted external Intents. Then you could invoke the webrtc app in Java from .NET. You would have to maintain your changes in the WebRTC app in Java and the changes in your own app in C#. Not totally unreasonable, but annoying and I really wanted to in time build an open source .NET library for WebRTC that could be plugged into MVVMCross so you just could add WebRTC support to your app either on IOS or Android easily.

The signaling would be located behind a nice interface, which you could implement as you liked, and there would be a reference signaling implementation in a PCL (portable class library) using websockets, for a peer-peer meeting.

My current schedule has brought me back to reality though, and currently I have not much time at all to dedicate to this. I will still try to reach the end goal but it will most likely take some time.

In addition - I would love to skip the C# - JBL (Java Binding Library)/JNI - JNI c++ wrapper - C++ and replace the JNI C++ wrapper with a clean wrapper that could be P/Invoked directly from C#, I think that currently is beyond my skills, I have not touched C++ in many years, and I do not know the WebRTC code base, so I think we need to expand the team a little to get this optimization in.

Any takers?
