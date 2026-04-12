export default [
    {
        ignores: ["node_modules/**", "dist/**"]
    },
    {
        files: ["**/*.js", "**/*.ts"],
        rules: {
            "no-unused-vars": "error",
            "no-console": "warn",
            "complexity": ["error", 10], // enforcing cyclomatic complexity
            "eqeqeq": "error",
            "no-var": "error",
            "prefer-const": "error"
        }
    }
];
