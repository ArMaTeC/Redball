i18n = import('i18n')

# i18n.gettext with empty languages will just generate the POT template
# Add .po files to the po directory for each language you want to support
i18n.gettext('redball',
  args: ['--directory=' + meson.project_source_root() / 'po'],
  preset: 'glib'
)
