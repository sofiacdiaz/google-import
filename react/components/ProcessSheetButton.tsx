/* eslint-disable @typescript-eslint/no-explicit-any */
import type { FC } from 'react'
import React, { Fragment, useState } from 'react'
import { useMutation } from 'react-apollo'
import { FormattedMessage } from 'react-intl'
import { Alert, Button, Card, Divider } from 'vtex.styleguide'

import M_PROCESS_SHEET from '../mutations/ProcessSheet.gql'

interface AlertParams {
  rowDone: number
  rowErrors: number
  showAlert: boolean
  setShowAlert: React.Dispatch<React.SetStateAction<boolean>>
  isBlocked: boolean
}

const successAlert = ({
  rowDone,
  rowErrors,
  showAlert,
  setShowAlert,
  isBlocked,
}: AlertParams) => {
  if (!showAlert) return null

  if (rowDone === 0 && rowErrors !== 0) return null

  return (
    <Fragment>
      <div className="mb2">
        <Alert type="success" onClose={() => setShowAlert(false)}>
          {/* If no error alert and no rows processed; give the user feedback that the process has run */}
          {isBlocked ? (
            <FormattedMessage id="admin/sheets-catalog-import.sheet-import.blocked" />
          ) : (
          rowDone === 0 ? (
            <FormattedMessage id="admin/sheets-catalog-import.sheet-import.no-change" />
          ) : (
            <FormattedMessage
              id="admin/sheets-catalog-import.sheet-import.done"
              values={{ done: rowDone }}
            />
           )
          )}
        </Alert>
      </div>
    </Fragment>
  )
}

const errorAlert = ({
  rowDone,
  rowErrors,
  showAlert,
  setShowAlert,
}: AlertParams) => {
  if (rowErrors === 0 || !showAlert) return null

  return (
    <Fragment>
      <div className="mb2">
        <Alert type="error" onClose={() => setShowAlert(false)}>
          {rowDone === 0 ? (
            <FormattedMessage
              id="admin/sheets-catalog-import.sheet-import.only-errors"
              values={{ errors: rowErrors }}
            />
          ) : (
            <FormattedMessage
              id="admin/sheets-catalog-import.sheet-import.error"
              values={{ errors: rowErrors }}
            />
          )}
        </Alert>
      </div>
    </Fragment>
  )
}

const ProcessSheetButton: FC = () => {
  const [showSuccess, setShowSuccess] = useState(true)
  const [showError, setShowError] = useState(true)
  const [sheetImport, { loading: sheetProcessing, data: sheetProcessed }] =
      useMutation<{
          processSheet: {
              done: number
              error: number
              message: string
              blocked: boolean
          }
      }>(M_PROCESS_SHEET)

  const rowErrors = sheetProcessed?.processSheet?.error ?? 0
  const rowDone = sheetProcessed?.processSheet?.done ?? 0
  const isImportBlocked = sheetProcessed?.processSheet?.blocked ?? false
  const displayAlerts = !sheetProcessing && sheetProcessed

  return (
    <div>
      {displayAlerts && (
        <Fragment>
          {successAlert({
            rowDone,
            rowErrors,
            showAlert: showSuccess,
            setShowAlert: setShowSuccess,
            isBlocked: isImportBlocked,
          })}
          {errorAlert({
            rowDone,
            rowErrors,
            showAlert: showError,
            setShowAlert: setShowError,
            isBlocked: isImportBlocked,
          })}
        </Fragment>
      )}
      <Card>
        <div className="flex">
          <div className="w-70">
            <p>
              <FormattedMessage id="admin/sheets-catalog-import.sheet-import.description" />
            </p>
          </div>
          <div
            style={{ flexGrow: 1 }}
            className="flex items-stretch w-20 justify-center"
          >
            <Divider orientation="vertical" />
          </div>
          <div className="w-30 items-center flex">
            <Button
              variation="secondary"
              collapseLeft
              block
              isLoading={sheetProcessing}
              onClick={() => {
                sheetImport()
                setShowSuccess(true)
                setShowError(true)
              }}
            >
              <FormattedMessage id="admin/sheets-catalog-import.sheet-import.button" />
            </Button>
          </div>
        </div>
      </Card>
      <br />
    </div>
  )
}

export default ProcessSheetButton
