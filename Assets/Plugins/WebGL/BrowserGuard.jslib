mergeInto(LibraryManager.library, {
  // ゲーム画面（canvas）上で右クリックメニューを出さない
  DisableContextMenu: function () {
    var c = (typeof Module !== 'undefined' && Module.canvas) || document.querySelector('canvas');
    if (!c) return;
    c.addEventListener('contextmenu', function (e) { e.preventDefault(); });
  }
});
