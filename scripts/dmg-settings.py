# dmgbuild settings for the MockiExams installer window. Run through scripts/make-dmg.sh.
import os.path

app = defines["app"]
app_name = os.path.basename(app)

format = "ULMO"
filesystem = "HFS+"
files = [app]
symlinks = {"Applications": "/Applications"}
hide_extensions = [app_name]
icon = defines["volume_icon"]
background = defines["background"]

window_rect = ((200, 120), (480, 352))
default_view = "icon-view"
show_status_bar = False
show_tab_view = False
show_toolbar = False
show_pathbar = False
show_sidebar = False
sidebar_width = 180

arrange_by = None
icon_size = 80
text_size = 12
label_pos = "bottom"
show_icon_preview = False
show_item_info = False
icon_locations = {
    app_name: (120, 160),
    "Applications": (360, 160),
}
