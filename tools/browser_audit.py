"""Exercise Botato terrain editing, navigation and export locally or against its public deployment."""
import functools
import http.server
import os
import threading
from pathlib import Path
from playwright.sync_api import sync_playwright
ROOT=Path(__file__).resolve().parents[1]
class Quiet(http.server.SimpleHTTPRequestHandler):
    def log_message(self,*args): pass
server=http.server.ThreadingHTTPServer(('127.0.0.1',0),functools.partial(Quiet,directory=str(ROOT/'examples/portfolio')))
threading.Thread(target=server.serve_forever,daemon=True).start()
try:
    with sync_playwright() as p:
        browser=p.chromium.launch(**({'channel':'chrome'} if os.name=='nt' else {}))
        page=browser.new_page(viewport={'width':1280,'height':1000},reduced_motion='reduce')
        errors=[];page.on('pageerror',lambda e:errors.append(str(e)))
        page.goto(os.environ.get('AUDIT_URL',f'http://127.0.0.1:{server.server_port}'),wait_until='networkidle')
        page.wait_for_function('window.__botato?.ready')
        assert page.evaluate('__botato.reachable')
        initial=page.evaluate('__botato.actor')
        page.wait_for_function('Math.hypot(__botato.actor[0]-5,__botato.actor[1]-25)>.5')
        page.locator('#pause').click()
        for name in ['ravine','ruins','quarry']:
            page.locator('#map').select_option(name)
            assert page.evaluate('__botato.reachable'),name
        page.locator('#world').focus();page.keyboard.press('ArrowUp')
        assert page.evaluate('__botato.goal[1]')==24
        before=page.evaluate('__botato.blocked')
        page.locator('[data-tool="wall"]').click()
        box=page.locator('#world').bounding_box()
        page.mouse.click(box['x']+box['width']*.94,box['y']+box['height']*.49)
        assert page.evaluate('__botato.blocked')>before
        assert not page.evaluate('__botato.reachable')
        page.locator('[data-tool="erase"]').click()
        page.mouse.click(box['x']+box['width']*.94,box['y']+box['height']*.49)
        assert page.evaluate('__botato.reachable')
        page.locator('details summary').click()
        page.locator('#clearance').fill('6');page.locator('#clearance').dispatch_event('input')
        page.locator('#explored').check()
        with page.expect_download() as dl:page.locator('#export').click()
        import json
        exported=json.loads(Path(dl.value.path()).read_text())
        assert exported['path'] and exported['clearance']==6
        page.locator('#reset').click()
        page.locator('[data-tool="goal"]').click()
        page.evaluate('window.scrollTo(0,0)')
        page.screenshot(path=str(ROOT/'examples/portfolio/preview.png'))
        page.set_viewport_size({'width':390,'height':844})
        assert page.evaluate('document.documentElement.scrollWidth<=innerWidth+1'),'mobile overflow'
        assert not errors,errors
        print('PASS: moving agent, three maps, obstacle edits, unreachable state, reroute, keyboard and export')
        browser.close()
finally: server.shutdown()
