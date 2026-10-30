Feature: Investigate MCP failures using operator file logs
  As a server operator
  I want a persistent log of call parameters, responses and technical errors
  So that I can investigate failures without putting debugging details in model responses

  Background:
    Given two configured MCP project directories

  Scenario Outline: Correlate parameters and replies for successful and failed calls
    When I make failing and successful MCP calls with "<mode>" file logging
    Then the file log identifies the calls and their exceptions
    And the file log contains the exact parameters and replies for each call
    And tool payloads are absent from console logs
    And MCP responses contain no logging configuration

    Examples:
      | mode     |
      | default  |
      | detailed |

  Scenario: Logging is not enabled by an ordinary server startup
    When I list MCP tools with "read-only" server startup
    Then no server log file was created

  Scenario Outline: Invalid file logging settings fail before protocol startup
    When I invoke serve with "<problem>"
    Then serve reports an error containing "<message>"

    Examples:
      | problem           | message                |
      | empty log path    | log file path          |
      | missing log path  | log-file               |
      | log is directory  | Cannot open log file   |
      | log parent is file| Cannot open log file   |
