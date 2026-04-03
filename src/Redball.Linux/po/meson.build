i18n = import('i18n')

# Add languages as needed
languages = ['en', 'de', 'fr', 'es', 'it', 'pt', 'nl', 'pl', 'ru', 'ja', 'zh']

foreach lang : languages
  i18n.gettext('redball',
    args: ['--directory=' + meson.source_root() / 'po'],
    preset: 'glib'
  )
endforeach
