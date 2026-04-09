project('redball', 'c',
  version : '2.1.482',
  meson_version : '>= 0.59.0',
  default_options : [ 'warning_level=2',
                      'werror=false',
                    ],
)

# Get the major/minor version for gschema
version_parts = meson.project_version().split('.')
major_version = version_parts[0]
minor_version = version_parts[1]

i18n = import('i18n')

# Python module installation
python = import('python').find_installation('python3', required: true)

# Dependencies
dependency('gtk4', version: '>= 4.6.0')
dependency('libadwaita-1', version: '>= 1.0.0')

# Application ID
app_id = 'com.armatec.Redball'

# Directories
prefix = get_option('prefix')
bindir = get_option('bindir')
datadir = get_option('datadir')
pkgdatadir = datadir / 'redball'
pkglibdir = prefix / get_option('libdir') / 'redball'

# Configuration data
conf = configuration_data()
conf.set('VERSION', meson.project_version())
conf.set('MAJOR_VERSION', major_version)
conf.set('MINOR_VERSION', minor_version)
conf.set('APP_ID', app_id)
conf.set('PYTHON', python.full_path())
conf.set('PKGDATADIR', pkgdatadir)
conf.set('PKGLIBDIR', pkglibdir)
conf.set('LOCALEDIR', get_option('localedir'))
conf.set('BINDIR', prefix / bindir)

# Install Python package
install_subdir('redball',
  install_dir: pkglibdir,
  exclude_directories: ['__pycache__', 'tests'],
  exclude_files: ['*.pyc', '*.pyo']
)

# Install launcher script
configure_file(
  input: 'redball.in',
  output: 'redball',
  configuration: conf,
  install: true,
  install_dir: bindir,
  install_mode: 'rwxr-xr-x'
)

# Install desktop file
desktop_file = configure_file(
  input: 'data' / 'com.armatec.Redball.desktop.in',
  output: 'com.armatec.Redball.desktop',
  configuration: conf,
  install: true,
  install_dir: datadir / 'applications'
)

# Validate desktop file
desktop_file_validate = find_program('desktop-file-validate', required: false)
if desktop_file_validate.found()
  test('validate-desktop',
    desktop_file_validate,
    args: [desktop_file]
  )
endif

# Install metainfo
configure_file(
  input: 'data' / 'com.armatec.Redball.metainfo.xml.in',
  output: 'com.armatec.Redball.metainfo.xml',
  configuration: conf,
  install: true,
  install_dir: datadir / 'metainfo'
)

# Install gschema
install_data('data' / 'com.armatec.Redball.gschema.xml',
  install_dir: datadir / 'glib-2.0' / 'schemas'
)

# Compile schemas at install
# gnome = import('gnome')
# gnome.compile_schemas()

# Install icons
icon_sizes = ['16', '24', '32', '48', '64', '128', '256']
foreach size : icon_sizes
  install_data('data' / 'icons' / 'hicolor' / size + 'x' + size / 'com.armatec.Redball.png',
    install_dir: datadir / 'icons' / 'hicolor' / size + 'x' + size / 'apps'
  )
endforeach

# Install scalable icon
install_data('data' / 'icons' / 'hicolor' / 'scalable' / 'com.armatec.Redball.svg',
  install_dir: datadir / 'icons' / 'hicolor' / 'scalable' / 'apps'
)

# i18n
subdir('po')

# Tests
subdir('tests')

# Summary
summary('Version', meson.project_version())
summary('Prefix', prefix)
summary('Data dir', pkgdatadir)
summary('Python', python.full_path())
