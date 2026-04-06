fs = import('fs')

test_env = environment()
test_env.set('PYTHONPATH', meson.project_source_root())

# Python unit tests
python_tests = [
  'test_keepawake.py',
  'test_config.py',
  'test_timer.py',
]

foreach test_file : python_tests
  if fs.exists(test_file)
    test(test_file,
      python,
      args: [meson.current_source_dir() / test_file],
      env: test_env,
      workdir: meson.current_source_dir()
    )
  endif
endforeach
