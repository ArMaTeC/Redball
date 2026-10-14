export default [
    {
        ignores: [
            "**/node_modules/**",
            "**/dist/**",
            "**/vendor/**",
            "**/*.min.js",
            "**/bin/**",
            "**/obj/**",
            "**/coverage*/**",
            "**/releases/**",
            "**/TestResults/**",
            "site/dist/**",
            "update-server/releases/**",
            "update-server/data/**",
            "update-server/logs/**",
            "web-admin/logs/**",
            "audit-run/**"
        ]
    },
    {
        files: ["**/*.js", "**/*.ts"],
        rules: {
            "no-unused-vars": "error",
            "complexity": ["error", 10], // enforcing cyclomatic complexity
            "eqeqeq": "error",
            "no-var": "error",
            "prefer-const": "error"
        }
    },
    {
        // Browser-facing code: console output leaks into the user's console.
        // Node servers/CLIs legitimately log via console, so the rule is
        // scoped to shipped browser surfaces only.
        files: [
            "**/public/**/*.js",
            "browser-extension/**/*.js",
            "site/src/**/*.{js,ts}"
        ],
        rules: {
            "no-console": "warn"
        }
    }
];
