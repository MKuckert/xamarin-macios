//
//  
//  AglContext.cs
//
//  Created by Erik Ylvisaker on 3/17/08.
//  Copyright 2008. All rights reserved.
//
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Control = System.Windows.Forms.Control;

namespace OpenTK.Platform.MacOS
{
    using Carbon;
    using Graphics;

    using AGLRendererInfo = IntPtr;
    using AGLPixelFormat = IntPtr;
    using AGLContext = IntPtr;
    using AGLPbuffer = IntPtr;

    class AglContext : DesktopGraphicsContext 
    {
        bool mVSync = false;
        // Todo: keep track of which display adapter was specified when the context was created.
        // IntPtr displayID;
        
        GraphicsMode graphics_mode;
        CarbonWindowInfo carbonWindow;
        IntPtr shareContextRef;

        public AglContext(GraphicsMode mode, IWindowInfo window, IGraphicsContext shareContext)
        {
            Debug.Print("Context Type: {0}", shareContext);
            Debug.Print("Window info: {0}", window);

            this.graphics_mode = mode;
            this.carbonWindow = (CarbonWindowInfo)window;

            if (shareContext is AglContext)
                shareContextRef = ((AglContext)shareContext).Handle.Handle;
            
            CreateContext(mode, carbonWindow, shareContextRef, true);
        }


        private void AddPixelAttrib(List<int> aglAttributes, Agl.PixelFormatAttribute pixelFormatAttribute)
        {
            Debug.Print(pixelFormatAttribute.ToString());

            aglAttributes.Add((int)pixelFormatAttribute);
        }
        private void AddPixelAttrib(List<int> aglAttributes, Agl.PixelFormatAttribute pixelFormatAttribute, int value)
        {
            Debug.Print("{0} : {1}", pixelFormatAttribute, value);

            aglAttributes.Add((int)pixelFormatAttribute);
            aglAttributes.Add(value);
        }
        void CreateContext(GraphicsMode mode, CarbonWindowInfo carbonWindow, 
            IntPtr shareContextRef, bool fullscreen)
        {
            List<int> aglAttributes = new  List<int>();
            
            Debug.Print("AGL pixel format attributes:");
            Debug.Indent();

            AddPixelAttrib(aglAttributes, Agl.PixelFormatAttribute.AGL_RGBA);
            AddPixelAttrib(aglAttributes, Agl.PixelFormatAttribute.AGL_DOUBLEBUFFER);
            AddPixelAttrib(aglAttributes, Agl.PixelFormatAttribute.AGL_RED_SIZE, mode.ColorFormat.Red);
            AddPixelAttrib(aglAttributes, Agl.PixelFormatAttribute.AGL_GREEN_SIZE, mode.ColorFormat.Green);
            AddPixelAttrib(aglAttributes, Agl.PixelFormatAttribute.AGL_BLUE_SIZE, mode.ColorFormat.Blue);
            AddPixelAttrib(aglAttributes, Agl.PixelFormatAttribute.AGL_ALPHA_SIZE, mode.ColorFormat.Alpha);

            if (mode.Depth > 0)
                AddPixelAttrib(aglAttributes, Agl.PixelFormatAttribute.AGL_DEPTH_SIZE, mode.Depth);

            if (mode.Stencil > 0)
                AddPixelAttrib(aglAttributes, Agl.PixelFormatAttribute.AGL_STENCIL_SIZE, mode.Stencil);

            if (mode.AccumulatorFormat.BitsPerPixel > 0)
            {
                AddPixelAttrib(aglAttributes, Agl.PixelFormatAttribute.AGL_ACCUM_RED_SIZE, mode.AccumulatorFormat.Red);
                AddPixelAttrib(aglAttributes, Agl.PixelFormatAttribute.AGL_ACCUM_GREEN_SIZE, mode.AccumulatorFormat.Green);
                AddPixelAttrib(aglAttributes, Agl.PixelFormatAttribute.AGL_ACCUM_BLUE_SIZE, mode.AccumulatorFormat.Blue);
                AddPixelAttrib(aglAttributes, Agl.PixelFormatAttribute.AGL_ACCUM_ALPHA_SIZE, mode.AccumulatorFormat.Alpha);
            }
            
            if (mode.Samples > 0)
            {
                AddPixelAttrib(aglAttributes, Agl.PixelFormatAttribute.AGL_SAMPLE_BUFFERS_ARB, 1);
                AddPixelAttrib(aglAttributes, Agl.PixelFormatAttribute.AGL_SAMPLES_ARB, mode.Samples);
            }

            if (fullscreen)
            {
                AddPixelAttrib(aglAttributes, Agl.PixelFormatAttribute.AGL_FULLSCREEN);
            }
            AddPixelAttrib(aglAttributes, Agl.PixelFormatAttribute.AGL_NONE);

            Debug.Unindent();

            Debug.Write("Attribute array:  ");
            for (int i = 0; i < aglAttributes.Count; i++)
                Debug.Write(aglAttributes[i].ToString() + "  ");
            Debug.WriteLine("");

            AGLPixelFormat myAGLPixelFormat;

            // Choose a pixel format with the attributes we specified.
            if (fullscreen)
            {
                IntPtr gdevice;
    
                OSStatus status = Carbon.API.DMGetGDeviceByDisplayID(
                    QuartzDisplayDeviceDriver.MainDisplay, out gdevice, false);
                
                if (status != OSStatus.NoError)
                    throw new MacOSException(status, "DMGetGDeviceByDisplayID failed.");

                myAGLPixelFormat = Agl.aglChoosePixelFormat(
                    ref gdevice, 1,
                    aglAttributes.ToArray());

                Agl.AglError err = Agl.GetError();

                if (err == Agl.AglError.BadPixelFormat)
                {
                    Debug.Print("Failed to create full screen pixel format.");
                    Debug.Print("Trying again to create a non-fullscreen pixel format.");

                    CreateContext(mode, carbonWindow, shareContextRef, false);
                    return;
                }
            }
            else
            {
                myAGLPixelFormat = Agl.aglChoosePixelFormat(
                    IntPtr.Zero, 0, 
                    aglAttributes.ToArray());

                MyAGLReportError("aglChoosePixelFormat");
            }
            
            
            // create the context and share it with the share reference.
            Handle = new ContextHandle( Agl.aglCreateContext(myAGLPixelFormat, shareContextRef));
            MyAGLReportError("aglCreateContext");

            // Free the pixel format from memory.
            Agl.aglDestroyPixelFormat(myAGLPixelFormat);
            MyAGLReportError("aglDestroyPixelFormat");

            Debug.Print("IsControl: {0}", carbonWindow.IsControl);
            
            SetDrawable(carbonWindow);
            SetBufferRect(carbonWindow);
            Update(carbonWindow);
            
            MakeCurrent(carbonWindow);

            Debug.Print("context: {0}", Handle.Handle);
        }

        void SetBufferRect(CarbonWindowInfo carbonWindow)
        {
            if (carbonWindow.IsControl == false)
                return;
            
            System.Windows.Forms.Control ctrl = Control.FromHandle(carbonWindow.WindowRef);
                
            if (ctrl.TopLevelControl == null)
                return;
                
            Rect rect = API.GetControlBounds(carbonWindow.WindowRef);
            System.Windows.Forms.Form frm = (System.Windows.Forms.Form) ctrl.TopLevelControl;

            System.Drawing.Point loc =
                frm.PointToClient(ctrl.PointToScreen(System.Drawing.Point.Empty));

            rect.X = (short)loc.X;
            rect.Y = (short)loc.Y;
            
            Debug.Print("Setting buffer_rect for control.");
            Debug.Print("MacOS Coordinate Rect:   {0}", rect);
            
            rect.Y = (short)(ctrl.TopLevelControl.ClientSize.Height - rect.Y - rect.Height);
            Debug.Print("  AGL Coordinate Rect:   {0}", rect);
            
            int[] glrect = new int[4];

            glrect[0] = rect.X;
            glrect[1] = rect.Y;
            glrect[2] = rect.Width;
            glrect[3] = rect.Height;

            Agl.aglSetInteger(Handle.Handle, Agl.ParameterNames.AGL_BUFFER_RECT, glrect);
            MyAGLReportError("aglSetInteger");

            Agl.aglEnable(Handle.Handle, Agl.ParameterNames.AGL_BUFFER_RECT);
            MyAGLReportError("aglEnable");
  
        }
        void SetDrawable(CarbonWindowInfo carbonWindow)
        {
            IntPtr windowPort = GetWindowPortForWindowInfo(carbonWindow);

            Agl.aglSetDrawable(Handle.Handle, windowPort);

            MyAGLReportError("aglSetDrawable");
        
        }

        private static IntPtr GetWindowPortForWindowInfo(CarbonWindowInfo carbonWindow)
        {
            IntPtr windowPort;
            if (carbonWindow.IsControl)
            {
                IntPtr controlOwner = API.GetControlOwner(carbonWindow.WindowRef);
                
                windowPort = API.GetWindowPort(controlOwner);
            }
            else
                windowPort = API.GetWindowPort(carbonWindow.WindowRef);
            return windowPort;
        }
        public override void Update(IWindowInfo window)      
        {
            CarbonWindowInfo carbonWindow = (CarbonWindowInfo)window;

            SetDrawable(carbonWindow);
            SetBufferRect(carbonWindow);

            Agl.aglUpdateContext(Handle.Handle);
        }

        void MyAGLReportError(string function)
        {
            Agl.AglError err = Agl.GetError();

            if (err != Agl.AglError.NoError)
                throw new MacOSException((OSStatus)err, string.Format(
                    "AGL Error from function {0}: {1}  {2}", 
                    function, err, Agl.ErrorString(err)));
        }

        bool firstFullScreen = false;

        internal void SetFullScreen(CarbonWindowInfo info)
        {
            Agl.aglSetFullScreen(Handle.Handle, 0, 0, 0, 0);

            // This is a weird hack to workaround a bug where the first time a context
            // is made fullscreen, we just end up with a blank screen.  So we undo it as fullscreen
            // and redo it as fullscreen.  
            if (firstFullScreen == false)
            {
                firstFullScreen = true;
                UnsetFullScreen(info);
                SetFullScreen(info);
            }
        }
        internal void UnsetFullScreen(CarbonWindowInfo windowInfo)
        {
            Agl.aglSetDrawable(Handle.Handle, IntPtr.Zero);
            SetDrawable(windowInfo);
        }


        #region IGraphicsContext Members

        bool firstSwap = false;
        public override void SwapBuffers()
        {
            // this is part of the hack to avoid dropping the first frame when
            // using multiple GLControls.
            if (firstSwap == false && carbonWindow.IsControl)
            {
                Debug.WriteLine("--> Resetting drawable. <--");
                firstSwap = true;
                SetDrawable(carbonWindow); 
                Update(carbonWindow);    
            }

            Agl.aglSwapBuffers(Handle.Handle);  
            MyAGLReportError("aglSwapBuffers");       
        }
        
        public override void MakeCurrent(IWindowInfo window)
        {
            if (Agl.aglSetCurrentContext(Handle.Handle) == false)
                MyAGLReportError("aglSetCurrentContext");
        }

        public override bool IsCurrent
        {
            get
            {
                return (Handle.Handle == Agl.aglGetCurrentContext());
            }
        }

        public override bool VSync
        {
            get
            {
                return mVSync;
            }
            set
            {
                int intVal = value ? 1 : 0;

                Agl.aglSetInteger(Handle.Handle, Agl.ParameterNames.AGL_SWAP_INTERVAL, ref intVal);

                mVSync = value;
            }
        }

        #endregion

        #region IDisposable Members
        ~AglContext()
        {
            Dispose(false);
        }

        public override void Dispose()
        {
            Dispose(true);
        }

        void Dispose(bool disposing)
        {
            if (IsDisposed || Handle.Handle == IntPtr.Zero)
                return;

            Debug.Print("Disposing of AGL context.");

            try
            {
                throw new Exception();
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.StackTrace);
            }

            Agl.aglSetCurrentContext(IntPtr.Zero);
            Agl.aglSetDrawable(Handle.Handle, IntPtr.Zero);

            Debug.Print("Set drawable to null for context {0}.", Handle.Handle);

            if (Agl.aglDestroyContext(Handle.Handle) == true)
            {
                Handle = ContextHandle.Zero;
                return;
            }

            // failed to destroy context.
            Debug.WriteLine("Failed to destroy context.");
            Debug.WriteLine(Agl.ErrorString(Agl.GetError()));

            // don't throw an exception from the finalizer thread.
            if (disposing)
            {
                throw new MacOSException((OSStatus)Agl.GetError(), Agl.ErrorString(Agl.GetError()));
            }

            IsDisposed = true;
        }

        #endregion

        #region IGraphicsContextInternal Members

        public override void LoadAll()
        {
            base.LoadAll();
        }

         private const string Library = "libdl.dylib";

        [DllImport(Library, EntryPoint = "NSIsSymbolNameDefined")]
        private static extern bool NSIsSymbolNameDefined(string s);
        [DllImport(Library, EntryPoint = "NSLookupAndBindSymbol")]
        private static extern IntPtr NSLookupAndBindSymbol(string s);
        [DllImport(Library, EntryPoint = "NSAddressOfSymbol")]
        private static extern IntPtr NSAddressOfSymbol(IntPtr symbol);

        public override IntPtr GetAddress(string function)
        {
            string fname = "_" + function;
            if (!NSIsSymbolNameDefined(fname))
                return IntPtr.Zero;

            IntPtr symbol = NSLookupAndBindSymbol(fname);
            if (symbol != IntPtr.Zero)
                symbol = NSAddressOfSymbol(symbol);

            return symbol;
        }

        #endregion
    }
}
