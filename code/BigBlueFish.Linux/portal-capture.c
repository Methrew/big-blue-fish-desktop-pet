// Modified by Methrew on 2026-11-15 for the Taiwan Traditional Chinese edition of Bai-Cute/big-blue-fish-desktop-pet 1.1.3, which also takes in acer1204's taiwan-edition fork (Apache-2.0).
/* Desktop-authorized screenshot bridge. DBus stays connected until Request.Response. */
#include <dbus/dbus.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include <time.h>

static const char *service="org.freedesktop.portal.Desktop", *object="/org/freedesktop/portal/desktop";
static unsigned property(DBusConnection *bus, const char *name) {
    DBusMessage *call=dbus_message_new_method_call(service,object,"org.freedesktop.DBus.Properties","Get");
    const char *interface="org.freedesktop.portal.Screenshot";
    dbus_message_append_args(call,DBUS_TYPE_STRING,&interface,DBUS_TYPE_STRING,&name,DBUS_TYPE_INVALID);
    DBusError error; dbus_error_init(&error);
    DBusMessage *reply=dbus_connection_send_with_reply_and_block(bus,call,10000,&error); dbus_message_unref(call);
    unsigned value=0; DBusMessageIter iter,variant;
    if(reply && dbus_message_iter_init(reply,&iter) && dbus_message_iter_get_arg_type(&iter)==DBUS_TYPE_VARIANT) {
        dbus_message_iter_recurse(&iter,&variant);
        if(dbus_message_iter_get_arg_type(&variant)==DBUS_TYPE_UINT32) dbus_message_iter_get_basic(&variant,&value);
    }
    if(reply) dbus_message_unref(reply);
    dbus_error_free(&error); return value;
}
static void option(DBusMessageIter *dict,const char *key,int type,const char *signature,void *value) {
    DBusMessageIter entry,variant;
    dbus_message_iter_open_container(dict,DBUS_TYPE_DICT_ENTRY,NULL,&entry);
    dbus_message_iter_append_basic(&entry,DBUS_TYPE_STRING,&key);
    dbus_message_iter_open_container(&entry,DBUS_TYPE_VARIANT,signature,&variant);
    dbus_message_iter_append_basic(&variant,type,value);
    dbus_message_iter_close_container(&entry,&variant); dbus_message_iter_close_container(dict,&entry);
}
int main(int argc,char **argv) {
    int foreground=argc==2 && !strcmp(argv[1],"--foreground");
    DBusError error; dbus_error_init(&error); DBusConnection *bus=dbus_bus_get(DBUS_BUS_SESSION,&error);
    if(!bus) { fprintf(stderr,"無法連線到桌面授權服務：%s\n",error.message);return 1; }
    unsigned version=property(bus,"version"),target=foreground?8:1;
    if(!version) { fputs("桌面截圖授權服務尚未就緒。\n",stderr);return 2; }
    if(foreground && (version<3 || !(property(bus,"AvailableTargets")&8))) {
        fputs("目前的 Wayland 桌面沒有提供前景視窗截圖介面；可以使用全螢幕截圖，或在 X11 桌面讀取前景視窗。\n",stderr);return 3;
    }
    dbus_bus_add_match(bus,"type='signal',interface='org.freedesktop.portal.Request',member='Response'",&error);
    if(dbus_error_is_set(&error)) { fputs(error.message,stderr);return 4; }
    DBusMessage *call=dbus_message_new_method_call(service,object,"org.freedesktop.portal.Screenshot","Screenshot");
    DBusMessageIter iter,dict; dbus_message_iter_init_append(call,&iter);const char *parent="";
    dbus_message_iter_append_basic(&iter,DBUS_TYPE_STRING,&parent);
    dbus_message_iter_open_container(&iter,DBUS_TYPE_ARRAY,"{sv}",&dict);
    dbus_bool_t no=FALSE; option(&dict,"interactive",DBUS_TYPE_BOOLEAN,"b",&no); option(&dict,"modal",DBUS_TYPE_BOOLEAN,"b",&no);
    if(version>=3) option(&dict,"target",DBUS_TYPE_UINT32,"u",&target);
    dbus_message_iter_close_container(&iter,&dict);
    DBusMessage *reply=dbus_connection_send_with_reply_and_block(bus,call,30000,&error); dbus_message_unref(call);
    if(!reply) { fprintf(stderr,"截圖要求未完成：%s\n",error.message);return 5; }
    const char *request=NULL;
    if(!dbus_message_get_args(reply,&error,DBUS_TYPE_OBJECT_PATH,&request,DBUS_TYPE_INVALID)) { fputs("截圖授權回應無效。\n",stderr);return 6; }
    char *handle=strdup(request); dbus_message_unref(reply);time_t started=time(NULL);
    while(time(NULL)-started<100 && dbus_connection_read_write(bus,200)) {
        DBusMessage *message;
        while((message=dbus_connection_pop_message(bus))) {
            if(!dbus_message_is_signal(message,"org.freedesktop.portal.Request","Response") || !dbus_message_get_path(message) || strcmp(dbus_message_get_path(message),handle)) { dbus_message_unref(message);continue; }
            unsigned response=2;DBusMessageIter result,entries,entry,variant;
            dbus_message_iter_init(message,&result);dbus_message_iter_get_basic(&result,&response);
            if(response) { fputs("使用者取消了截圖授權，或桌面截圖失敗。\n",stderr);dbus_message_unref(message);free(handle);return 7; }
            dbus_message_iter_next(&result);dbus_message_iter_recurse(&result,&entries);
            while(dbus_message_iter_get_arg_type(&entries)==DBUS_TYPE_DICT_ENTRY) {
                const char *key=NULL;dbus_message_iter_recurse(&entries,&entry);dbus_message_iter_get_basic(&entry,&key);dbus_message_iter_next(&entry);
                if(!strcmp(key,"uri")) {
                    const char *uri=NULL;dbus_message_iter_recurse(&entry,&variant);dbus_message_iter_get_basic(&variant,&uri);
                    if(!uri || strncmp(uri,"file://",7)) { fputs("截圖不是本機檔案。\n",stderr);return 8; }
                    char *path=strdup(uri+7),*out=path;
                    for(const char *in=uri+7;*in;in++) { unsigned value;if(*in=='%' && in[1] && in[2] && sscanf(in+1,"%2x",&value)==1){*out++=(char)value;in+=2;}else *out++=*in; }*out=0;
                    FILE *file=fopen(path,"rb");if(!file){perror("讀取桌面截圖");return 9;}
                    unsigned char buffer[65536];size_t read;while((read=fread(buffer,1,sizeof buffer,file))) if(fwrite(buffer,1,read,stdout)!=read)break;
                    int failed=ferror(file)||ferror(stdout);fclose(file);unlink(path);free(path);free(handle);dbus_message_unref(message);dbus_connection_unref(bus);return failed?10:0;
                }
                dbus_message_iter_next(&entries);
            }
            dbus_message_unref(message);fputs("桌面截圖沒有傳回影像。\n",stderr);return 11;
        }
    }
    fputs("等待桌面截圖授權逾時。\n",stderr);free(handle);dbus_connection_unref(bus);return 12;
}
