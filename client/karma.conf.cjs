/**
 * Karma configuration for DevHub.
 * - ChromeHeadless launcher for local watch mode (developer feedback).
 * - ChromeHeadlessCI launcher with --no-sandbox for CI containers
 *   (GitHub Actions, GitLab runners, Dockerized CI agents).
 *
 * Run with: `npm run test` (watch) or `npm run test:ci` (single shot, coverage).
 */
module.exports = function (config) {
  // Note: Angular 20's @angular/build karma builder injects its own framework + asset middleware
  // at runtime. We only need to declare jasmine here — the builder handles the rest.
  config.set({
    basePath: '',
    frameworks: ['jasmine'],
    plugins: [
      require('karma-jasmine'),
      require('karma-chrome-launcher'),
      require('karma-jasmine-html-reporter'),
      require('karma-coverage'),
    ],
    client: {
      jasmine: {
        random: false,
      },
      clearContext: false,
    },
    jasmineHtmlReporter: {
      suppressAll: true,
    },
    coverageReporter: {
      dir: require('path').join(__dirname, './coverage/dev-hub'),
      subdir: '.',
      reporters: [
        { type: 'html' },
        { type: 'text-summary' },
        { type: 'lcovonly', file: 'lcov.info' },
      ],
    },
    reporters: ['progress', 'kjhtml'],
    port: 9876,
    colors: true,
    logLevel: config.LOG_INFO,
    restartOnFileChange: true,
    browsers: ['ChromeHeadless'],
    customLaunchers: {
      ChromeHeadlessCI: {
        base: 'ChromeHeadless',
        // --no-sandbox: required to run in unprivileged CI containers.
        // --disable-gpu: avoid the gpu-process crash log noise on headless Linux.
        // --disable-dev-shm-usage: small /dev/shm on default CI runners would OOM Chrome otherwise.
        flags: ['--no-sandbox', '--disable-gpu', '--disable-dev-shm-usage'],
      },
    },
    singleRun: false,
  });
};
