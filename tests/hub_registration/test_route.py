import importlib.util
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('capture_route', Path(__file__).resolve().parents[2] / 'scripts/hub-registration/add_capture_route.py')
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)


class RouteTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.path = self.root / 'studio.conf'
        self.block = '''    location = /desktop/cutwork {
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_pass http://127.0.0.1:8769;
    }'''
        self.original = 'server {\n    server_name example.test;\n' + self.block + '\n}\n'
        self.path.write_text(self.original)
        self.path.chmod(0o640)
        self.dump = '# configuration file ' + str(self.path) + ':\n' + self.original
        self.calls = []

    def fake_run(self, args, **kwargs):
        self.calls.append(args)
        return subprocess.CompletedProcess(args, 0, stdout=self.dump)

    def test_copies_upgrade_headers_into_same_server_and_preserves_original(self):
        with patch.object(m.os, 'fchown'):
            m.repair(self.fake_run, self.root)
        self.assertEqual(self.original.replace(self.block, self.block + '\n\n' + self.block.replace('/desktop/cutwork', '/desktop/capture')), self.path.read_text())
        self.assertEqual(0o640, self.path.stat().st_mode & 0o777)
        self.assertEqual(['systemctl', 'reload', 'nginx'], self.calls[-1])
        self.assertEqual(self.original, next(self.root.glob('flamoris-*/original.conf')).read_text())

    def test_syntax_failure_restores_original_and_reloads_it(self):
        def fail(args, **kwargs):
            result = self.fake_run(args, **kwargs)
            if args == ['nginx', '-t'] and self.calls.count(args) == 1:
                raise subprocess.CalledProcessError(1, args)
            return result
        with patch.object(m.os, 'fchown'), self.assertRaisesRegex(RuntimeError, 'original_restored'):
            m.repair(fail, self.root)
        self.assertEqual(self.original, self.path.read_text())
        self.assertEqual(['systemctl', 'reload', 'nginx'], self.calls[-1])

    def test_reload_failure_also_restores_original(self):
        def fail(args, **kwargs):
            result = self.fake_run(args, **kwargs)
            if args[0] == 'systemctl' and self.calls.count(args) == 1:
                raise subprocess.CalledProcessError(1, args)
            return result
        with patch.object(m.os, 'fchown'), self.assertRaisesRegex(RuntimeError, 'original_restored'):
            m.repair(fail, self.root)
        self.assertEqual(self.original, self.path.read_text())

    def test_existing_route_is_refused_without_changes(self):
        self.dump += self.block.replace('/desktop/cutwork', '/desktop/capture')
        with self.assertRaisesRegex(RuntimeError, 'capture_route_exists'):
            m.repair(self.fake_run, self.root)
        self.assertEqual(self.original, self.path.read_text())
        self.assertEqual(1, len(self.calls))

    def test_ambiguous_nested_or_other_proxy_layout_is_refused(self):
        for text in [self.dump + self.dump,
                     self.dump.replace('proxy_http_version 1.1;', 'if ($a) { return 403; }'),
                     self.dump.replace(':8769;', ':9000;')]:
            with self.subTest(text=text), self.assertRaises(RuntimeError):
                m.proposed(text)


if __name__ == '__main__':
    unittest.main()
