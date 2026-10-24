#define UNICODE
#define _UNICODE
#include <windows.h>
#include <stddef.h>
#ifdef XIAOMA_GLYPH_HOME
#include "HomeGlyphs.generated.h"
#endif
#ifndef XIAOMA_VERSION
#define XIAOMA_VERSION "0.6.11"
#endif
#define HOME_HANDOFF 0x8031
#define HOME_BACKEND_CLOSED 0x8032
static UINT ui_dpi=96,display_dpi=96;typedef UINT (WINAPI *window_dpi_fn)(HWND);static window_dpi_fn window_dpi;static int px(int value){return MulDiv(value,ui_dpi,96);}static HFONT normal,large,small;static HBRUSH body,bar,button,record_brush;static int logged;static wchar_t logfile[2048];static ULONGLONG entry_ms,paths_ms,mutex_ms,fonts_ms,class_ms,create_ms,dwm_ms,show_ms,paint_begin_ms,body_ms,buttons_ms,complete_paint_ms,controls_ready_ms;
static wchar_t core_path[2048],root_path[2048],profile_path[2048],class_name[100],initial_tail[32768];static HWND main_window,buttons[5];static int handed_off,closing,hidden_test,paint_done,button_paints;
typedef void (WINAPI *drag_accept_fn)(HWND,BOOL);typedef UINT (WINAPI *drag_query_fn)(HANDLE,UINT,LPWSTR,UINT);typedef void (WINAPI *drag_finish_fn)(HANDLE);
typedef HRESULT (WINAPI *dwm_set_fn)(HWND,DWORD,LPCVOID,DWORD);
static drag_accept_fn drag_accept;static drag_query_fn drag_query;static drag_finish_fn drag_finish;
typedef struct{HWND window;wchar_t tail[32768];} CoreRequest;
static size_t wide_length(const wchar_t* value){const wchar_t* end=value;while(*end)end++;return end-value;}
static wchar_t* wide_copy(wchar_t* target,const wchar_t* value){wchar_t* out=target;while((*out++=*value++));return target;}
static wchar_t* wide_append(wchar_t* target,const wchar_t* value){wide_copy(target+wide_length(target),value);return target;}
static void wide_copy_n(wchar_t* target,const wchar_t* value,size_t count){size_t i=0;for(;i<count&&value[i];i++)target[i]=value[i];target[i]=0;}
static int wide_compare(const wchar_t* left,const wchar_t* right){while(*left&&*left==*right){left++;right++;}return *left-*right;}
static wchar_t* wide_last(wchar_t* value,wchar_t find){wchar_t* result=0;for(;*value;value++)if(*value==find)result=value;return result;}
static int wide_contains(const wchar_t* value,const wchar_t* find){for(;*value;value++){const wchar_t* a=value;const wchar_t* b=find;while(*b&&*a==*b){a++;b++;}if(!*b)return 1;}return 0;}
static void* zero_alloc(size_t count,size_t size){return HeapAlloc(GetProcessHeap(),HEAP_ZERO_MEMORY,count*size);}
static void heap_free(void* value){if(value)HeapFree(GetProcessHeap(),0,value);}
void* memset(void* value,int fill,size_t size){unsigned char* out=value;for(size_t i=0;i<size;i++)out[i]=(unsigned char)fill;return value;}
void* memmove(void* target,const void* source,size_t size){unsigned char* out=target;const unsigned char* in=source;if(out<in){for(size_t i=0;i<size;i++)out[i]=in[i];}else{while(size){size--;out[size]=in[size];}}return target;}
static char* ascii_append(char* out,const char* value){while(*value)*out++=*value++;*out=0;return out;}
static char* ascii_number(char* out,ULONGLONG value){char digits[24];int count=0;do{digits[count++]=(char)('0'+value%10);value/=10;}while(value);while(count)*out++=digits[--count];*out=0;return out;}
static char* ascii_time(char* out,ULONGLONG ticks){out=ascii_number(out,ticks/10000);*out++='.';ULONGLONG fraction=ticks%10000/10;*out++=(char)('0'+fraction/100);*out++=(char)('0'+fraction/10%10);*out++=(char)('0'+fraction%10);*out=0;return out;}
static void wide_number(wchar_t* out,ULONGLONG value){char data[24];ascii_number(data,value);for(int i=0;;i++){out[i]=(wchar_t)data[i];if(!data[i])break;}}
static ULONGLONG elapsed(){FILETIME born,end,kernel,user,now;ULARGE_INTEGER a,b;GetProcessTimes(GetCurrentProcess(),&born,&end,&kernel,&user);GetSystemTimeAsFileTime(&now);a.LowPart=born.dwLowDateTime;a.HighPart=born.dwHighDateTime;b.LowPart=now.dwLowDateTime;b.HighPart=now.dwHighDateTime;return b.QuadPart-a.QuadPart;}
static void quote_append(wchar_t* target,const wchar_t* value){if(target[0])wide_append(target,L" ");wide_append(target,L"\"");int slashes=0;for(;*value;value++){if(*value==L'\\'){slashes++;continue;}if(*value==L'\"'){while(slashes-->0)wide_append(target,L"\\\\");wide_append(target,L"\\\"");}else{while(slashes-->0)wide_append(target,L"\\");int n=wide_length(target);target[n]=*value;target[n+1]=0;}slashes=0;}while(slashes-->0)wide_append(target,L"\\\\");wide_append(target,L"\"");}
static DWORD WINAPI core_request(void* data){CoreRequest* request=(CoreRequest*)data;if(wide_length(request->tail)+2*wide_length(core_path)+100>=32768){heap_free(request);return 1;}wchar_t* command=HeapAlloc(GetProcessHeap(),0,32768*sizeof(wchar_t));if(!command){heap_free(request);return 1;}command[0]=0;quote_append(command,core_path);if(request->window){wchar_t flag[80];wide_copy(flag,L" --home-window=");wide_number(flag+wide_length(flag),(ULONGLONG)(ULONG_PTR)request->window);wide_append(command,flag);}if(request->tail[0]){wide_append(command,L" ");wide_append(command,request->tail);}STARTUPINFOW info;PROCESS_INFORMATION child;ZeroMemory(&info,sizeof(info));ZeroMemory(&child,sizeof(child));info.cb=sizeof(info);info.dwFlags=STARTF_USESHOWWINDOW;info.wShowWindow=SW_SHOWNOACTIVATE;BOOL launched=CreateProcessW(core_path,command,0,0,FALSE,0,0,root_path,&info,&child);if(launched){CloseHandle(child.hThread);CloseHandle(child.hProcess);}else if(request->window&&IsWindow(request->window)){MessageBoxW(request->window,L"\u64ad\u653e\u5668\u6587\u4ef6\u4e0d\u5b8c\u6574\uff0c\u8bf7\u91cd\u65b0\u89e3\u538b\u5b89\u88c5\u5305\u3002",L"\u5c0f\u9a6c\u64ad\u653e\u5668",MB_OK|MB_ICONERROR);}heap_free(command);heap_free(request);return launched?0:1;}
static void launch_core(HWND owner,const wchar_t* tail){CoreRequest* request=zero_alloc(1,sizeof(CoreRequest));if(!request)return;request->window=owner;if(tail)wide_copy_n(request->tail,tail,32760);HANDLE thread=CreateThread(0,0,core_request,request,0,0);if(thread)CloseHandle(thread);else heap_free(request);}
static void action(const wchar_t* name){wchar_t value[100];wide_copy(value,L"--home-action=");wide_append(value,name);launch_core(main_window,value);}
static DWORD WINAPI foreground(void* window){if(IsWindow((HWND)window))SetForegroundWindow((HWND)window);return 0;}
static wchar_t* command_tail(){wchar_t* p=GetCommandLineW();if(*p==L'\"'){p++;while(*p&&*p!=L'\"')p++;if(*p)p++;}else while(*p&&*p!=L' ')p++;while(*p==L' ')p++;return p;}
static void text(HDC dc,const wchar_t* value,int x,int y,int w,int h,HFONT font,COLORREF color,UINT align){
#ifdef XIAOMA_GLYPH_HOME
 for(int i=0;i<sizeof(home_glyphs)/sizeof(home_glyphs[0]);i++){HomeGlyph* glyph=home_glyphs+i;if(wide_compare(value,glyph->label))continue;if(!glyph->pixels){int total=glyph->width*glyph->height;glyph->pixels=HeapAlloc(GetProcessHeap(),0,total*4);if(!glyph->pixels)break;int filled=0;for(const unsigned int* run=glyph->runs;filled<total;run++){unsigned int color=*run&0xffffff,count=*run>>24;while(count--&&filled<total)glyph->pixels[filled++]=color;}}BITMAPINFO info;ZeroMemory(&info,sizeof(info));info.bmiHeader.biSize=sizeof(BITMAPINFOHEADER);info.bmiHeader.biWidth=glyph->width;info.bmiHeader.biHeight=-glyph->height;info.bmiHeader.biPlanes=1;info.bmiHeader.biBitCount=32;int gw=px(glyph->width),gh=px(glyph->height),left=x+(align==DT_CENTER?(w-gw)/2:align==DT_RIGHT?w-gw:0);SetStretchBltMode(dc,COLORONCOLOR);StretchDIBits(dc,left,y+(h-gh)/2,gw,gh,0,0,glyph->width,glyph->height,glyph->pixels,&info,DIB_RGB_COLORS,SRCCOPY);return;}
#endif
 RECT r={x,y,x+w,y+h};if(!font)font=GetStockObject(DEFAULT_GUI_FONT);SelectObject(dc,font);SetTextColor(dc,color);SetBkMode(dc,TRANSPARENT);DrawTextW(dc,value,-1,&r,DT_SINGLELINE|DT_VCENTER|align);
}
static void home_text(HDC dc,const wchar_t* value,int x,int y,int w,int h,HFONT font,COLORREF color,UINT align){text(dc,value,px(x),px(y),px(w),px(h),font,color,align);}

static HWND hot_button;static WNDPROC button_proc_original;
static LRESULT CALLBACK glass_button_proc(HWND window,UINT message,WPARAM w,LPARAM l){
 if(message==WM_MOUSEMOVE&&hot_button!=window){HWND old=hot_button;hot_button=window;TRACKMOUSEEVENT track={sizeof(track),TME_LEAVE,window,0};TrackMouseEvent(&track);if(old)InvalidateRect(old,0,FALSE);InvalidateRect(window,0,FALSE);}
 if(message==WM_MOUSELEAVE){if(hot_button==window)hot_button=0;InvalidateRect(window,0,FALSE);}
 if(message==WM_SETCURSOR){SetCursor(LoadCursorW(0,IDC_HAND));return TRUE;}
 return CallWindowProcW(button_proc_original,window,message,w,l);
}
static void glass_button(HDC dc,const RECT* bounds,int hover,int pressed,int accent){
 RECT area;area.left=bounds->left;area.top=bounds->top;area.right=bounds->right;area.bottom=bounds->bottom;
 typedef COLORREF (WINAPI *brush_color_fn)(HDC,COLORREF);static brush_color_fn set_brush_color;if(!set_brush_color)set_brush_color=(brush_color_fn)GetProcAddress(GetModuleHandleW(L"gdi32.dll"),"SetDCBrushColor");if(!set_brush_color)return;
 int inset=px(1);InflateRect(&area,-inset,-inset);int h=area.bottom-area.top;
 if(h<2||area.right<=area.left)return;
 int saved=SaveDC(dc);HRGN clip=CreateRoundRectRgn(area.left,area.top,area.right,area.bottom,h,h);SelectClipRgn(dc,clip);
 HBRUSH brush=GetStockObject(DC_BRUSH);
 int rt=pressed?31:hover?48:36,gt=pressed?48:hover?65:51,bt=pressed?68:hover?85:70;
 int rb=pressed?19:hover?38:29,gb=pressed?32:hover?54:43,bb=pressed?49:hover?73:61;
 if(accent){rt=pressed?139:hover?183:166;gt=pressed?38:hover?57:47;bt=pressed?56:hover?78:67;rb=132;gb=35;bb=50;}
 for(int y=0;y<h;y++){int gloss=0;set_brush_color(dc,RGB(rt+(rb-rt)*y/h+gloss,gt+(gb-gt)*y/h+gloss,bt+(bb-bt)*y/h+gloss));RECT line={area.left,area.top+y,area.right,area.top+y+1};FillRect(dc,&line,brush);}
 RestoreDC(dc,saved);DeleteObject(clip);
 HGDIOBJ old_brush=SelectObject(dc,GetStockObject(NULL_BRUSH));HPEN edge=CreatePen(PS_SOLID,1,RGB(60,76,96));HGDIOBJ old_pen=SelectObject(dc,edge);RoundRect(dc,area.left,area.top,area.right,area.bottom,h,h);SelectObject(dc,old_pen);DeleteObject(edge);
 SelectObject(dc,old_brush);
}

static void create_buttons(HWND window){const wchar_t* labels[]={L"\u25cf \u5f55\u5c4f",L"\u6587\u4ef6",L"\u6587\u4ef6\u5939",L"\u4e5f\u53ef\u4ee5\u9009\u62e9\u6587\u4ef6",L"\u68c0\u67e5\u66f4\u65b0"};RECT area;GetClientRect(window,&area);int width=MulDiv(area.right,96,ui_dpi),height=MulDiv(area.bottom,96,ui_dpi),center=height/2-115;RECT targets[]={{width-300,12,90,34},{width-202,12,90,34},{width-104,12,90,34},{width/2-80,center+185,160,40},{width-120,height-38,100,38}};for(int i=0;i<5;i++)buttons[i]=CreateWindowExW(0,L"BUTTON",labels[i],WS_CHILD|WS_VISIBLE|WS_TABSTOP|BS_OWNERDRAW,px(targets[i].left),px(targets[i].top),px(targets[i].right),px(targets[i].bottom),window,(HMENU)(ULONG_PTR)(i+1),GetModuleHandleW(0),0);for(int i=0;i<5;i++){WNDPROC previous=(WNDPROC)SetWindowLongPtrW(buttons[i],GWLP_WNDPROC,(LONG_PTR)glass_button_proc);if(!button_proc_original)button_proc_original=previous;}}
static void layout_buttons(HWND window){RECT area;GetClientRect(window,&area);int width=MulDiv(area.right,96,ui_dpi),height=MulDiv(area.bottom,96,ui_dpi),center=height/2-115;BOOL redraw=IsWindowVisible(window);MoveWindow(buttons[0],px(width-300),px(12),px(90),px(34),redraw);MoveWindow(buttons[1],px(width-202),px(12),px(90),px(34),redraw);MoveWindow(buttons[2],px(width-104),px(12),px(90),px(34),redraw);MoveWindow(buttons[3],px(width/2-80),px(center+185),px(160),px(40),redraw);MoveWindow(buttons[4],px(width-120),px(height-38),px(100),px(38),redraw);}
#ifdef XIAOMA_TEST_CAPTURE
static void capture_home(HWND window){wchar_t file[2048]={0};GetEnvironmentVariableW(L"XIAOMA_HOME_CAPTURE",file,2048);if(!file[0])return;RECT r;GetClientRect(window,&r);HDC source=GetDC(window),memory=CreateCompatibleDC(source);BITMAPINFO info;ZeroMemory(&info,sizeof(info));info.bmiHeader.biSize=sizeof(BITMAPINFOHEADER);info.bmiHeader.biWidth=r.right;info.bmiHeader.biHeight=-r.bottom;info.bmiHeader.biPlanes=1;info.bmiHeader.biBitCount=32;void* pixels=0;HBITMAP bitmap=CreateDIBSection(source,&info,DIB_RGB_COLORS,&pixels,0,0);if(!bitmap){DeleteDC(memory);ReleaseDC(window,source);return;}HGDIOBJ previous=SelectObject(memory,bitmap);BitBlt(memory,0,0,r.right,r.bottom,source,0,0,SRCCOPY);GdiFlush();BITMAPFILEHEADER header;ZeroMemory(&header,sizeof(header));header.bfType=0x4d42;header.bfOffBits=sizeof(header)+sizeof(BITMAPINFOHEADER);DWORD bytes=r.right*r.bottom*4;header.bfSize=header.bfOffBits+bytes;HANDLE output=CreateFileW(file,GENERIC_WRITE,0,0,CREATE_ALWAYS,FILE_ATTRIBUTE_NORMAL,0);if(output!=INVALID_HANDLE_VALUE){DWORD written;WriteFile(output,&header,sizeof(header),&written,0);WriteFile(output,&info.bmiHeader,sizeof(info.bmiHeader),&written,0);WriteFile(output,pixels,bytes,&written,0);CloseHandle(output);}SelectObject(memory,previous);DeleteObject(bitmap);DeleteDC(memory);ReleaseDC(window,source);}
#endif
static void stamp(){if(!complete_paint_ms){GdiFlush();complete_paint_ms=elapsed();}char data[1200];char* p=data;p=ascii_append(p,"{\"version\":\"");p=ascii_append(p,XIAOMA_VERSION);p=ascii_append(p,"\",\"paintMs\":");p=ascii_time(p,complete_paint_ms?complete_paint_ms:elapsed());p=ascii_append(p,",\"pid\":");p=ascii_number(p,GetCurrentProcessId());p=ascii_append(p,",\"entryMs\":");p=ascii_time(p,entry_ms);p=ascii_append(p,",\"pathsMs\":");p=ascii_time(p,paths_ms);p=ascii_append(p,",\"mutexMs\":");p=ascii_time(p,mutex_ms);p=ascii_append(p,",\"fontsMs\":");p=ascii_time(p,fonts_ms);p=ascii_append(p,",\"classMs\":");p=ascii_time(p,class_ms);p=ascii_append(p,",\"createMs\":");p=ascii_time(p,create_ms);p=ascii_append(p,",\"dwmMs\":");p=ascii_time(p,dwm_ms);p=ascii_append(p,",\"showMs\":");p=ascii_time(p,show_ms);p=ascii_append(p,",\"paintBeginMs\":");p=ascii_time(p,paint_begin_ms);p=ascii_append(p,",\"bodyMs\":");p=ascii_time(p,body_ms);p=ascii_append(p,",\"buttonsMs\":");p=ascii_time(p,buttons_ms);p=ascii_append(p,",\"controlsReadyMs\":");p=ascii_time(p,controls_ready_ms);p=ascii_append(p,"}");if(logfile[0]){CreateDirectoryW(profile_path,0);HANDLE file=CreateFileW(logfile,GENERIC_WRITE,FILE_SHARE_READ,0,CREATE_ALWAYS,FILE_ATTRIBUTE_NORMAL,0);if(file!=INVALID_HANDLE_VALUE){DWORD written;WriteFile(file,data,(DWORD)(p-data),&written,0);CloseHandle(file);}}}
static LRESULT CALLBACK window_proc(HWND window,UINT message,WPARAM w,LPARAM l){
 if(message==WM_CREATE){if(window_dpi){UINT found=window_dpi(window);if(found)ui_dpi=found>display_dpi?found:display_dpi;}
create_buttons(window);
 return 0;}
 if(message==WM_SIZE){if(buttons[0])layout_buttons(window);return 0;}
 if(message==WM_DRAWITEM){DRAWITEMSTRUCT* item=(DRAWITEMSTRUCT*)l;if(item->CtlID>=1&&item->CtlID<=5){
  FillRect(item->hDC,&item->rcItem,item->CtlID==4?body:bar);glass_button(item->hDC,&item->rcItem,hot_button==item->hwndItem,(item->itemState&ODS_SELECTED)!=0,item->CtlID==1);
  if(!normal)normal=CreateFontW(-px(15),0,0,0,FW_NORMAL,0,0,0,DEFAULT_CHARSET,0,0,CLEARTYPE_QUALITY,0,L"Microsoft YaHei UI");
  wchar_t caption[90];GetWindowTextW(item->hwndItem,caption,90);SelectObject(item->hDC,normal);SetBkMode(item->hDC,TRANSPARENT);SetTextColor(item->hDC,RGB(247,249,255));RECT caption_rect=item->rcItem;if(item->itemState&ODS_SELECTED)OffsetRect(&caption_rect,0,px(1));DrawTextW(item->hDC,caption,-1,&caption_rect,DT_SINGLELINE|DT_CENTER|DT_VCENTER);
  if(item->itemState&ODS_FOCUS){RECT focus=item->rcItem;InflateRect(&focus,-px(7),-px(6));DrawFocusRect(item->hDC,&focus);}
  button_paints|=1<<(item->CtlID-1);if(!logged&&button_paints==31)buttons_ms=elapsed();if(paint_done&&button_paints==31&&!logged){logged=1;stamp();}return TRUE;}}

 if(message==WM_COMMAND&&HIWORD(w)==BN_CLICKED){switch(LOWORD(w)){case 1:action(L"record");break;case 2:case 4:action(L"files");break;case 3:action(L"folder");break;case 5:action(L"update");break;}return 0;}
 if(message==WM_PAINT){if(!paint_begin_ms)paint_begin_ms=elapsed();PAINTSTRUCT paint;HDC dc=BeginPaint(window,&paint);RECT r;GetClientRect(window,&r);FillRect(dc,&r,body);int width=MulDiv(r.right,96,ui_dpi),height=MulDiv(r.bottom,96,ui_dpi);RECT head={0,0,r.right,px(58)},foot={0,r.bottom-px(38),r.right,r.bottom};FillRect(dc,&head,bar);FillRect(dc,&foot,bar);
  home_text(dc,L"\u5c0f\u9a6c\u64ad\u653e\u5668+\u5f55\u5c4f",20,0,260,58,normal,RGB(231,232,236),DT_LEFT);
  home_text(dc,L"\u25cf \u5f55\u5c4f",width-300,0,90,58,normal,RGB(205,208,216),DT_CENTER);home_text(dc,L"\u6587\u4ef6",width-202,0,90,58,normal,RGB(205,208,216),DT_CENTER);home_text(dc,L"\u6587\u4ef6\u5939",width-104,0,90,58,normal,RGB(205,208,216),DT_CENTER);
  int center=height/2-115;HPEN pen=CreatePen(PS_SOLID,1,RGB(78,83,93));HGDIOBJ previous_pen=SelectObject(dc,pen),previous_brush=SelectObject(dc,GetStockObject(NULL_BRUSH));int dc_state=SaveDC(dc);SetGraphicsMode(dc,GM_ADVANCED);XFORM transform={(FLOAT)ui_dpi/96,0,0,(FLOAT)ui_dpi/96,0,0};SetWorldTransform(dc,&transform);Rectangle(dc,width/2-23,center-40,width/2+23,center+6);MoveToEx(dc,width/2-22,center-5,0);LineTo(dc,width/2-9,center-18);LineTo(dc,width/2+4,center-7);LineTo(dc,width/2+13,center-13);LineTo(dc,width/2+22,center-4);Ellipse(dc,width/2+5,center-31,width/2+13,center-23);RestoreDC(dc,dc_state);SelectObject(dc,previous_pen);SelectObject(dc,previous_brush);DeleteObject(pen);
  home_text(dc,L"\u62d6\u5165\u6587\u4ef6\uff0c\u5f00\u59cb\u67e5\u770b",width/2-300,center+25,600,55,large,RGB(231,232,236),DT_CENTER);
  home_text(dc,L"\u6587\u4ef6\u3001\u6587\u4ef6\u5939\uff0c\u6216\u5e8f\u5217\u4e2d\u7684\u4efb\u610f\u4e00\u5e27",width/2-300,center+80,600,35,normal,RGB(176,190,208),DT_CENTER);
  home_text(dc,L"\u56fe\u7247       \u89c6\u9891       \u97f3\u9891       \u5e8f\u5217\u5e27       \u6a21\u578b       \u6587\u6863",width/2-300,center+125,600,35,small,RGB(159,176,198),DT_CENTER);
  RECT open={px(width/2-80),px(center+185),px(width/2+80),px(center+225)};FillRect(dc,&open,button);home_text(dc,L"\u4e5f\u53ef\u4ee5\u9009\u62e9\u6587\u4ef6",width/2-80,center+185,160,40,normal,RGB(25,27,32),DT_CENTER);
  home_text(dc,L"\u652f\u6301\u76f4\u63a5\u62d6\u5165\u6587\u4ef6\u4e0e\u6587\u4ef6\u5939",18,height-38,400,38,small,RGB(176,190,208),DT_LEFT);home_text(dc,L"\u68c0\u67e5\u66f4\u65b0",width-120,height-38,100,38,small,RGB(153,158,169),DT_RIGHT);EndPaint(window,&paint);if(!body_ms)body_ms=elapsed();paint_done=1;
 if(button_paints==31&&!logged){logged=1;stamp();}

 return 0;
 }
 if(message==WM_LBUTTONUP){RECT r;GetClientRect(window,&r);int x=MulDiv((short)LOWORD(l),96,ui_dpi),y=MulDiv((short)HIWORD(l),96,ui_dpi);r.right=MulDiv(r.right,96,ui_dpi);r.bottom=MulDiv(r.bottom,96,ui_dpi);int center=r.bottom/2-115;if(y<58){if(x>=r.right-104)action(L"folder");else if(x>=r.right-202)action(L"files");else if(x>=r.right-300)action(L"record");}else if(y>=r.bottom-38&&x>=r.right-140)action(L"update");else if(x>=r.right/2-80&&x<=r.right/2+80&&y>=center+185&&y<=center+225)action(L"files");return 0;}
 if(message==WM_KEYDOWN){if(w=='O'&&(GetKeyState(VK_CONTROL)&0x8000)){action((GetKeyState(VK_SHIFT)&0x8000)?L"folder":L"files");return 0;}if(w==VK_ESCAPE)return 0;}
 if(message==WM_DROPFILES&&drag_query){HANDLE drop=(HANDLE)w;UINT count=drag_query(drop,0xffffffff,0,0);wchar_t* tail=HeapAlloc(GetProcessHeap(),0,32768*sizeof(wchar_t));wchar_t* file=HeapAlloc(GetProcessHeap(),0,2048*sizeof(wchar_t));if(!tail||!file){heap_free(tail);heap_free(file);drag_finish(drop);return 0;}tail[0]=0;for(UINT i=0;i<count;i++){drag_query(drop,i,file,2048);if(wide_length(tail)+wide_length(file)*2+8<32700)quote_append(tail,file);}drag_finish(drop);launch_core(window,tail);heap_free(tail);heap_free(file);return 0;}
 if(message==HOME_HANDOFF||message==HOME_BACKEND_CLOSED){handed_off=1;DestroyWindow(window);return 0;}
 if(message==0x02e0){ui_dpi=LOWORD(w);RECT* next=(RECT*)l;SetWindowPos(window,0,next->left,next->top,next->right-next->left,next->bottom-next->top,SWP_NOZORDER|SWP_NOACTIVATE);layout_buttons(window);InvalidateRect(window,0,TRUE);return 0;}
 if(message==WM_GETMINMAXINFO){MINMAXINFO* sizes=(MINMAXINFO*)l;sizes->ptMinTrackSize.x=680;sizes->ptMinTrackSize.y=480;return 0;}
 if(message==WM_CLOSE){closing=1;if(!handed_off)launch_core(0,L"--home-close");DestroyWindow(window);return 0;}if(message==WM_DESTROY){PostQuitMessage(0);return 0;}return DefWindowProcW(window,message,w,l);
}
int WINAPI WinMain(HINSTANCE instance,HINSTANCE old,LPSTR args,int show){
 entry_ms=elapsed();
 typedef BOOL (WINAPI *dpi_aware_fn)(void);dpi_aware_fn dpi_aware=(dpi_aware_fn)GetProcAddress(GetModuleHandleW(L"user32.dll"),"SetProcessDPIAware");typedef BOOL (WINAPI *dpi_context_fn)(HANDLE);dpi_context_fn dpi_context=(dpi_context_fn)GetProcAddress(GetModuleHandleW(L"user32.dll"),"SetProcessDpiAwarenessContext");if(dpi_context)dpi_context((HANDLE)(LONG_PTR)-4);else if(dpi_aware)dpi_aware();window_dpi=(window_dpi_fn)GetProcAddress(GetModuleHandleW(L"user32.dll"),"GetDpiForWindow");
 GetModuleFileNameW(instance,root_path,2048);wchar_t* slash=wide_last(root_path,L'\\');if(slash)*slash=0;wide_copy(core_path,root_path);wide_append(core_path,L"\\\u5c0f\u9a6c\u64ad\u653e\u5668\u6838\u5fc3.exe");GetEnvironmentVariableW(L"YIYAN_TEST_USER_DATA",profile_path,2048);if(!profile_path[0]){wide_copy(profile_path,root_path);wide_append(profile_path,L"\\.data");}wide_copy_n(initial_tail,command_tail(),32760);if(wide_contains(initial_tail,L"--test-close")){CoreRequest* req=zero_alloc(1,sizeof(CoreRequest));wide_copy(req->tail,L"--test-close");core_request(req);return 0;}
 const wchar_t* file_argument=initial_tail;if(*file_argument==L'"')file_argument++;if(*file_argument&&*file_argument!=L'-'){CoreRequest* request=zero_alloc(1,sizeof(CoreRequest));if(!request)return 1;wide_copy(request->tail,initial_tail);return (int)core_request(request);}
 paths_ms=elapsed();unsigned long hash=2166136261UL;for(wchar_t* p=root_path;*p;p++)hash=(hash^(unsigned short)*p)*16777619UL;for(wchar_t* p=profile_path;*p;p++)hash=(hash^(unsigned short)*p)*16777619UL;wide_copy(class_name,L"XiaomaFastHome-");int hex_start=wide_length(class_name);for(int i=0;i<8;i++)class_name[hex_start+i]=L"0123456789abcdef"[(hash>>(28-i*4))&15];class_name[hex_start+8]=0;wchar_t mutex_name[130];wide_copy(mutex_name,L"Local\\");wide_append(mutex_name,class_name);HANDLE mutex=CreateMutexW(0,TRUE,mutex_name);if(GetLastError()==ERROR_ALREADY_EXISTS){HWND current=FindWindowW(class_name,0);if(initial_tail[0]){CoreRequest* request=zero_alloc(1,sizeof(CoreRequest));request->window=current;wide_copy(request->tail,initial_tail);core_request(request);}else if(current){if(IsIconic(current))ShowWindow(current,SW_RESTORE);SetForegroundWindow(current);}CloseHandle(mutex);return 0;}
 mutex_ms=elapsed();GetEnvironmentVariableW(L"XIAOMA_STARTUP_LOG",logfile,2048);if(!logfile[0]){wide_copy(logfile,profile_path);wide_append(logfile,L"\\home-startup.json");}body=CreateSolidBrush(RGB(11,18,32));bar=CreateSolidBrush(RGB(17,29,48));button=CreateSolidBrush(RGB(224,226,230));record_brush=CreateSolidBrush(RGB(112,46,60));
#ifndef XIAOMA_GLYPH_HOME
normal=CreateFontW(-15,0,0,0,FW_NORMAL,0,0,0,DEFAULT_CHARSET,0,0,CLEARTYPE_QUALITY,0,L"Microsoft YaHei UI");large=CreateFontW(-26,0,0,0,FW_NORMAL,0,0,0,DEFAULT_CHARSET,0,0,CLEARTYPE_QUALITY,0,L"Microsoft YaHei UI");small=CreateFontW(-13,0,0,0,FW_NORMAL,0,0,0,DEFAULT_CHARSET,0,0,CLEARTYPE_QUALITY,0,L"Microsoft YaHei UI");
#endif

 fonts_ms=elapsed();WNDCLASSW wc={0};wc.lpfnWndProc=window_proc;wc.hInstance=instance;wc.hCursor=LoadCursorW(0,IDC_ARROW);static wchar_t icon_path[2048];wide_copy(icon_path,root_path);wide_append(icon_path,L"\\resources\\app\\assets\\icon.ico");wc.hIcon=(HICON)LoadImageW(0,icon_path,IMAGE_ICON,0,0,LR_LOADFROMFILE|LR_DEFAULTSIZE);wc.hbrBackground=body;wc.lpszClassName=class_name;RegisterClassW(&wc);class_ms=elapsed();POINT cursor={0};GetCursorPos(&cursor);MONITORINFO monitor;ZeroMemory(&monitor,sizeof(monitor));monitor.cbSize=sizeof(monitor);GetMonitorInfoW(MonitorFromPoint(cursor,MONITOR_DEFAULTTONEAREST),&monitor);int width,height,available_w=monitor.rcWork.right-monitor.rcWork.left,available_h=monitor.rcWork.bottom-monitor.rcWork.top;display_dpi=(UINT)(available_w*96/1920);UINT height_dpi=(UINT)(available_h*96/1080);if(height_dpi<display_dpi)display_dpi=height_dpi;if(display_dpi<96)display_dpi=96;if(display_dpi>192)display_dpi=192;width=available_w*9/10;height=available_h*9/10;int x=monitor.rcWork.left+(available_w-width)/2,y=monitor.rcWork.top+(available_h-height)/2;wchar_t test[12]={0};GetEnvironmentVariableW(L"YIYAN_TEST_MODE",test,12);wchar_t visible[12]={0};GetEnvironmentVariableW(L"YIYAN_TEST_VISIBLE",visible,12);hidden_test=test[0]&&visible[0]!=L'1';if(hidden_test){x=-20000;y=-20000;}HWND window=CreateWindowExW(hidden_test?WS_EX_TOOLWINDOW:0,wc.lpszClassName,L"\u5c0f\u9a6c\u64ad\u653e\u5668+\u5f55\u5c4f",WS_OVERLAPPEDWINDOW,x,y,width,height,0,0,instance,0);main_window=window;create_ms=elapsed();HMODULE dwm=LoadLibraryW(L"dwmapi.dll");if(dwm){dwm_set_fn set=(dwm_set_fn)GetProcAddress(dwm,"DwmSetWindowAttribute");if(set){int dark=1;DWORD background=RGB(17,29,48),ink=RGB(231,232,236);set(window,20,&dark,4);set(window,35,&background,4);set(window,36,&ink,4);}}dwm_ms=elapsed();ShowWindow(window,SW_SHOWNOACTIVATE);show_ms=elapsed();UpdateWindow(window);


 
#ifdef XIAOMA_TEST_CAPTURE
 capture_home(window);
#endif
 HMODULE shell=LoadLibraryW(L"shell32.dll");if(shell){drag_accept=(drag_accept_fn)GetProcAddress(shell,"DragAcceptFiles");drag_query=(drag_query_fn)GetProcAddress(shell,"DragQueryFileW");drag_finish=(drag_finish_fn)GetProcAddress(shell,"DragFinish");if(drag_accept)drag_accept(window,TRUE);}launch_core(window,initial_tail);if(!hidden_test&&!test[0]){HANDLE activate=CreateThread(0,0,foreground,window,0,0);if(activate)CloseHandle(activate);}MSG msg;while(GetMessageW(&msg,0,0,0)>0){if(msg.message==WM_KEYDOWN&&msg.wParam=='O'&&(GetKeyState(VK_CONTROL)&0x8000)){action((GetKeyState(VK_SHIFT)&0x8000)?L"folder":L"files");continue;}if(!IsDialogMessageW(window,&msg)){TranslateMessage(&msg);DispatchMessageW(&msg);}}ReleaseMutex(mutex);CloseHandle(mutex);return 0;
}
#ifdef XIAOMA_NO_CRT
void _winstart(void){ExitProcess(WinMain(GetModuleHandleW(0),0,0,SW_SHOWNORMAL));}
#endif
